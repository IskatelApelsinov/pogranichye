// pogserver — сервер живого мира: по умолчанию поднимает HTTP+WebSocket, к которому
// подключается тонкий клиент (и позже Unity). Флаг -demo гоняет мир вхолостую и печатает
// «отчёт о жизни» (контраст с мёртвым прототипом).
//
//	go run ./cmd/pogserver                         # сервер на :8090, тонкий клиент из ../thin-client/web
//	go run ./cmd/pogserver -addr :9000 -villages 12
//	go run ./cmd/pogserver -demo -days 3000        # headless-прогон с отчётом
package main

import (
	"flag"
	"fmt"
	"log"
	"os"

	"pogranichye/core"
	"pogranichye/memory"
	"pogranichye/server"
	"pogranichye/sim"
)

func main() {
	var (
		addr     = flag.String("addr", ":8090", "адрес HTTP/WebSocket")
		seed     = flag.String("seed", "пограничье-1", "семя мира")
		villages = flag.Int("villages", 10, "сколько поселений")
		web      = flag.String("web", "../thin-client/web", "каталог тонкого клиента")
		demo     = flag.Bool("demo", false, "headless-прогон с отчётом вместо сервера")
		days     = flag.Int("days", 2000, "дней для -demo")
		verbose  = flag.Bool("v", false, "в -demo печатать выборку жителей")
	)
	flag.Parse()

	if *demo {
		runDemo(*seed, *villages, *days, *verbose)
		return
	}
	secret := []byte(os.Getenv("POG_SECRET")) // в проде задать; иначе эфемерный ключ
	srv := server.New(*seed, *villages, "accounts.json", secret, *web)
	log.Printf("Приграничье: сервер на %s, мир «%s» (%d поселений). Открой http://localhost%s/",
		*addr, *seed, *villages, *addr)
	log.Fatal(srv.Run(*addr))
}

func runDemo(seed string, villages, days int, verbose bool) {
	w, hub := sim.Generate(seed, villages)
	r := core.NewRunnerWith(hub, core.Clock{}, sim.Default()...)
	fmt.Printf("Старт: %s\n", sim.Describe(w))
	r.Run(w, days*core.HoursPerDay)
	fmt.Printf("\nПрожито дней: %d\n%s\n\n", days, sim.Describe(w))
	sim.Report(w, r.Clock)
	if verbose {
		sim.Sample(w, 30)
	}
	if blob, err := memory.Save(w, r.Clock, r.Hub); err == nil {
		fmt.Printf("\nСнимок мира: %d КБ (ничего не потеряно)\n", len(blob)/1024)
	}
}
