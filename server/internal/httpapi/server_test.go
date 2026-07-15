package httpapi

import (
	"io"
	"log/slog"
	"net/http"
	"net/http/httptest"
	"path/filepath"
	"testing"

	"github.com/September6969/MidnightVault/server/internal/store"
)

func TestHealth(t *testing.T) {
	database, err := store.Open(filepath.Join(t.TempDir(), "test.db"))
	if err != nil {
		t.Fatal(err)
	}
	defer database.Close()

	handler := New(database, slog.New(slog.NewTextHandler(io.Discard, nil)), "secret")
	request := httptest.NewRequest(http.MethodGet, "/healthz", nil)
	response := httptest.NewRecorder()

	handler.ServeHTTP(response, request)

	if response.Code != http.StatusOK {
		t.Fatalf("期望状态码 200，实际为 %d", response.Code)
	}
}

func TestProtectedEndpointRequiresToken(t *testing.T) {
	database, err := store.Open(filepath.Join(t.TempDir(), "test.db"))
	if err != nil {
		t.Fatal(err)
	}
	defer database.Close()

	handler := New(database, slog.New(slog.NewTextHandler(io.Discard, nil)), "secret")
	request := httptest.NewRequest(http.MethodPost, "/v1/devices", nil)
	request.RemoteAddr = "203.0.113.10:12345"
	response := httptest.NewRecorder()

	handler.ServeHTTP(response, request)

	if response.Code != http.StatusUnauthorized {
		t.Fatalf("期望状态码 401，实际为 %d", response.Code)
	}
}
