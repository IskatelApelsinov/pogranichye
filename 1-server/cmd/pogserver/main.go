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
	"os/signal"
	"syscall"
	"time"

	"pogranichye/core"
	"pogranichye/dice"
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
		starter  = flag.Bool("starter", false, "ОДНА стартовая деревня (для тонкого клиента/тестов)")
		tickMs   = flag.Int("tick", 0, "мс реального времени на игровой час (0 = по умолчанию 400; для демо, напр. 1500 — время суток читается)")
	)
	frontierMax := flag.Int("frontier-max", 300, "бесконечный мир: потолок числа поселений (0 — мир не растёт)")
	frontierVillages := flag.Int("frontier-villages", 4, "бесконечный мир: деревень в каждом новом краю")
	savePath := flag.String("save", "world.sav", "файл сохранения мира (\"\" — не сохранять)")
	saveEvery := flag.Int("save-every", 120, "автосохранение мира, секунд")
	flag.Parse()

	if *demo {
		runDemo(*seed, *villages, *days, *verbose, *starter)
		return
	}
	secret := []byte(os.Getenv("POG_SECRET")) // в проде задать; иначе эфемерный ключ
	var srv *server.Server
	if *starter {
		srv = server.NewStarter(*seed, "accounts.json", secret, *web)
		log.Printf("Приграничье: сервер на %s, СТАРТОВАЯ деревня. Открой http://localhost%s/", *addr, *addr)
	} else {
		srv = server.New(*seed, *villages, "accounts.json", secret, *web)
		log.Printf("Приграничье: сервер на %s, мир «%s» (%d поселений). Открой http://localhost%s/",
			*addr, *seed, *villages, *addr)
	}
	srv.SetFrontier(*frontierMax, *frontierVillages)
	if *tickMs > 0 {
		srv.SetTick(time.Duration(*tickMs) * time.Millisecond)
		log.Printf("Демо-темп: %d мс на игровой час", *tickMs)
	}
	if *savePath != "" {
		if ok, err := srv.LoadFrom(*savePath); err != nil {
			log.Fatalf("сохранение %s не читается: %v (удали файл, чтобы начать новый мир)", *savePath, err)
		} else if ok {
			log.Printf("мир поднят из сохранения %s", *savePath)
		} else {
			log.Printf("сохранения нет — новый мир; будет записан в %s", *savePath)
		}
		done := make(chan struct{})
		go func() { srv.Autosave(*savePath, time.Duration(*saveEvery)*time.Second); close(done) }()
		sig := make(chan os.Signal, 1)
		signal.Notify(sig, os.Interrupt, syscall.SIGTERM)
		go func() { // Ctrl+C / остановка контейнера: дописать мир и выйти
			<-sig
			srv.Close()
			<-done
			log.Printf("мир сохранён в %s", *savePath)
			os.Exit(0)
		}()
	}
	log.Fatal(srv.Run(*addr))
}

func runDemo(seed string, villages, days int, verbose, starter bool) {
	var w *core.World
	var hub *dice.Hub
	if starter {
		w, hub = sim.GenerateStarter(seed)
	} else {
		w, hub = sim.Generate(seed, villages)
	}
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
