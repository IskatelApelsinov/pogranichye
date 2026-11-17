package sim

import (
	"math"
	"sort"

	"pogranichye/core"
)

// Дороги края (v1.12) — серверная основа региональной карты и пути. Поселения по одну сторону гор
// связаны сетью трактов: каждое соединено с ближайшими соседями, сеть связна. Путь (op:"journey")
// идёт ПО ТРАКТАМ через промежуточные сёла, его длина — сумма перегонов. Идти можно туда, о чём
// знаешь (был, слышал) или что стоит на соседнем перегоне — туда ведёт сама дорога. Карта края в
// кадре (me.region) показывает только известное: остальное — туман.

// Road — перегон между двумя поселениями.
type Road struct {
	A, B core.ID
	Len  float64
}

// RoadGraph — сеть трактов между поселениями той же стороны гор, что и side. Детерминирована:
// минимальное остовное дерево (связность) + у каждого поселения тракт к двум ближайшим.
func RoadGraph(w *core.World, outer bool) []Road {
	type node struct {
		id   core.ID
		x, y float64
	}
	var ns []node
	w.Each("settlement", func(se *core.Entity) {
		if s := core.Settle(se); isOuter(w, s) == outer {
			ns = append(ns, node{se.ID, s.X, s.Y})
		}
	})
	sort.Slice(ns, func(i, j int) bool { return ns[i].id < ns[j].id })
	dist := func(i, j int) float64 { return math.Hypot(ns[i].x-ns[j].x, ns[i].y-ns[j].y) }
	seen := map[[2]core.ID]bool{}
	var out []Road
	add := func(i, j int) {
		a, b := ns[i].id, ns[j].id
		if a > b {
			a, b = b, a
		}
		if i == j || seen[[2]core.ID{a, b}] {
			return
		}
		seen[[2]core.ID{a, b}] = true
		out = append(out, Road{a, b, math.Max(1, dist(i, j))})
	}
	if len(ns) < 2 {
		return out
	}
	in := make([]bool, len(ns)) // остов (Прим)
	in[0] = true
	for k := 1; k < len(ns); k++ {
		bi, bj, bd := -1, -1, math.MaxFloat64
		for i := range ns {
			if !in[i] {
				continue
			}
			for j := range ns {
				if !in[j] {
					if d := dist(i, j); d < bd {
						bi, bj, bd = i, j, d
					}
				}
			}
		}
		in[bj] = true
		add(bi, bj)
	}
	for i := range ns { // по два ближайших соседа
		idx := make([]int, 0, len(ns)-1)
		for j := range ns {
			if j != i {
				idx = append(idx, j)
			}
		}
		sort.Slice(idx, func(a, b int) bool { return dist(i, idx[a]) < dist(i, idx[b]) })
		for k := 0; k < 2 && k < len(idx); k++ {
			add(i, idx[k])
		}
	}
	sort.Slice(out, func(i, j int) bool {
		if out[i].A != out[j].A {
			return out[i].A < out[j].A
		}
		return out[i].B < out[j].B
	})
	return out
}

// RoadRoute — кратчайший путь по трактам (список поселений от from до to включительно) и его длина.
func RoadRoute(roads []Road, from, to core.ID) ([]core.ID, float64) {
	adj := map[core.ID][]Road{}
	for _, r := range roads {
		adj[r.A] = append(adj[r.A], r)
		adj[r.B] = append(adj[r.B], Road{r.B, r.A, r.Len})
	}
	distTo := map[core.ID]float64{from: 0}
	prev := map[core.ID]core.ID{}
	done := map[core.ID]bool{}
	for {
		var cur core.ID
		best := math.MaxFloat64
		for id, d := range distTo {
			if !done[id] && (d < best || (d == best && id < cur)) {
				cur, best = id, d
			}
		}
		if best == math.MaxFloat64 {
			return nil, 0
		}
		if cur == to {
			break
		}
		done[cur] = true
		for _, r := range adj[cur] {
			nd := best + r.Len
			if old, ok := distTo[r.B]; !ok || nd < old {
				distTo[r.B], prev[r.B] = nd, cur
			}
		}
	}
	path := []core.ID{to}
	for at := to; at != from; {
		at = prev[at]
		path = append([]core.ID{at}, path...)
	}
	return path, distTo[to]
}

func knowsPlace(e *core.Entity, id core.ID) bool {
	k := Knowledge(e)
	return k != nil && (k.Places[id].Known || k.Places[id].Visited)
}

// RegionView — карта края для кадра (me.region): известные поселения, соседи по тракту (безымянные,
// пока о них не слышал) и перегоны между показанными узлами. Всё остальное — туман.
func RegionView(w *core.World, e *core.Entity) map[string]any {
	pl := core.Place(e)
	here := w.Get(pl.Settlement)
	hs := core.Settle(here)
	if hs == nil {
		return map[string]any{"nodes": []any{}, "roads": []any{}}
	}
	roads := RoadGraph(w, isOuter(w, hs))
	show := map[core.ID]string{here.ID: "here"}
	k := Knowledge(e)
	w.Each("settlement", func(se *core.Entity) {
		if se.ID == here.ID || isOuter(w, core.Settle(se)) != isOuter(w, hs) || k == nil {
			return
		}
		if k.Places[se.ID].Visited {
			show[se.ID] = "visited"
		} else if k.Places[se.ID].Known {
			show[se.ID] = "known"
		}
	})
	for _, r := range roads { // куда ведут тракты из известных мест — видно, но без имени
		for _, p := range [][2]core.ID{{r.A, r.B}, {r.B, r.A}} {
			if st := show[p[0]]; (st == "here" || st == "visited") && show[p[1]] == "" {
				show[p[1]] = "unknown"
			}
		}
	}
	ids := make([]core.ID, 0, len(show))
	for id := range show {
		ids = append(ids, id)
	}
	sort.Slice(ids, func(i, j int) bool { return ids[i] < ids[j] })
	nodes := make([]map[string]any, 0, len(ids))
	for _, id := range ids {
		s := core.Settle(w.Get(id))
		n := map[string]any{"id": eidStr(id), "x": math.Round(s.X), "y": math.Round(s.Y), "state": show[id], "name": "", "tier": ""}
		if show[id] != "unknown" {
			n["name"], n["tier"] = s.Name, s.Tier
		}
		nodes = append(nodes, n)
	}
	rs := []map[string]any{}
	for _, r := range roads {
		if show[r.A] != "" && show[r.B] != "" && (show[r.A] != "unknown" || show[r.B] != "unknown") {
			rs = append(rs, map[string]any{"a": eidStr(r.A), "b": eidStr(r.B), "len": math.Round(r.Len)})
		}
	}
	return map[string]any{"nodes": nodes, "roads": rs}
}
