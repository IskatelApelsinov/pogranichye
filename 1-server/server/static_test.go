package server

import (
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"testing"
)

func TestThinClientServesIndexAndSVGAssets(t *testing.T) {
	webDir := t.TempDir()
	if err := os.WriteFile(filepath.Join(webDir, "index.html"), []byte("<!doctype html><title>Thin client</title>"), 0o600); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(filepath.Join(webDir, "village-scene.svg"), []byte("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>"), 0o600); err != nil {
		t.Fatal(err)
	}

	h := (&Server{webDir: webDir}).Handler()
	index := httptest.NewRecorder()
	h.ServeHTTP(index, httptest.NewRequest(http.MethodGet, "/", nil))
	if index.Code != http.StatusOK || index.Body.Len() == 0 {
		t.Fatalf("index: status=%d body=%q", index.Code, index.Body.String())
	}
	if index.Header().Get("Content-Type") == "" {
		t.Fatal("index response must have a content type")
	}

	asset := httptest.NewRecorder()
	h.ServeHTTP(asset, httptest.NewRequest(http.MethodGet, "/village-scene.svg", nil))
	if asset.Code != http.StatusOK {
		t.Fatalf("SVG: expected 200, got %d", asset.Code)
	}
	if got := asset.Header().Get("Content-Type"); got != "image/svg+xml" {
		t.Fatalf("SVG content type = %q, want image/svg+xml", got)
	}
	if asset.Body.Len() == 0 {
		t.Fatal("SVG response is empty")
	}
}

func TestThinClientUnknownAssetIsNotFound(t *testing.T) {
	h := (&Server{webDir: t.TempDir()}).Handler()
	rec := httptest.NewRecorder()
	h.ServeHTTP(rec, httptest.NewRequest(http.MethodGet, "/missing.svg", nil))
	if rec.Code != http.StatusNotFound {
		t.Fatalf("expected 404 for missing asset, got %d", rec.Code)
	}
}

func TestThinClientDoesNotServeAssetsWithoutWebDirectory(t *testing.T) {
	h := (&Server{}).Handler()
	rec := httptest.NewRecorder()
	h.ServeHTTP(rec, httptest.NewRequest(http.MethodGet, "/village-scene.svg", nil))
	if rec.Code != http.StatusNotFound {
		t.Fatalf("expected 404 without web directory, got %d", rec.Code)
	}
}
