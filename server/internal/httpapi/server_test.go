package httpapi

import (
	"context"
	"crypto/ecdsa"
	"crypto/elliptic"
	"crypto/rand"
	"crypto/sha256"
	"crypto/x509"
	"encoding/base64"
	"io"
	"log/slog"
	"net/http"
	"net/http/httptest"
	"path/filepath"
	"strconv"
	"strings"
	"testing"
	"time"

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

func TestUnconfiguredTokenDoesNotAcceptRemoteEmptyBearer(t *testing.T) {
	database, err := store.Open(filepath.Join(t.TempDir(), "test.db"))
	if err != nil {
		t.Fatal(err)
	}
	defer database.Close()

	handler := New(database, slog.New(slog.NewTextHandler(io.Discard, nil)), "")
	request := httptest.NewRequest(http.MethodPost, "/v1/devices", nil)
	request.RemoteAddr = "203.0.113.10:12345"
	request.Header.Set("Authorization", "Bearer ")
	response := httptest.NewRecorder()

	handler.ServeHTTP(response, request)

	if response.Code != http.StatusUnauthorized {
		t.Fatalf("期望状态码 401，实际为 %d", response.Code)
	}
}

func TestTaskReportRejectsNegativeCounts(t *testing.T) {
	database, err := store.Open(filepath.Join(t.TempDir(), "test.db"))
	if err != nil {
		t.Fatal(err)
	}
	defer database.Close()

	handler := New(database, slog.New(slog.NewTextHandler(io.Discard, nil)), "secret")
	privateKey := registerSigningDevice(t, database, "device-a")
	body := `{"taskId":"task-a","deviceId":"device-a","status":"running","successCount":-1,"skippedCount":0,"failedCount":0,"totalBytes":0}`
	request := signedRequest(t, http.MethodPost, "/v1/tasks/report", body, "device-a", "negative-count-nonce", privateKey)
	response := httptest.NewRecorder()

	handler.ServeHTTP(response, request)

	if response.Code != http.StatusBadRequest {
		t.Fatalf("期望状态码 400，实际为 %d", response.Code)
	}
}

func TestSignedTaskReportRejectsReplay(t *testing.T) {
	database, err := store.Open(filepath.Join(t.TempDir(), "test.db"))
	if err != nil {
		t.Fatal(err)
	}
	defer database.Close()

	handler := New(database, slog.New(slog.NewTextHandler(io.Discard, nil)), "secret")
	privateKey := registerSigningDevice(t, database, "device-a")
	body := `{"taskId":"task-a","deviceId":"device-a","status":"completed","successCount":1,"skippedCount":0,"failedCount":0,"totalBytes":123}`
	request := signedRequest(t, http.MethodPost, "/v1/tasks/report", body, "device-a", "unique-replay-nonce", privateKey)
	response := httptest.NewRecorder()
	handler.ServeHTTP(response, request)
	if response.Code != http.StatusNoContent {
		t.Fatalf("首次签名请求期望 204，实际为 %d", response.Code)
	}

	replayed := signedRequest(t, http.MethodPost, "/v1/tasks/report", body, "device-a", "unique-replay-nonce", privateKey)
	replayResponse := httptest.NewRecorder()
	handler.ServeHTTP(replayResponse, replayed)
	if replayResponse.Code != http.StatusConflict {
		t.Fatalf("重放请求期望 409，实际为 %d", replayResponse.Code)
	}
}

func TestCommandFlowAndRevocation(t *testing.T) {
	database, err := store.Open(filepath.Join(t.TempDir(), "test.db"))
	if err != nil {
		t.Fatal(err)
	}
	defer database.Close()

	handler := New(database, slog.New(slog.NewTextHandler(io.Discard, nil)), "secret")
	privateKey := registerSigningDevice(t, database, "device-a")
	commandRequest := httptest.NewRequest(
		http.MethodPost,
		"/v1/commands",
		strings.NewReader(`{"deviceId":"device-a","type":"backup-now"}`))
	commandRequest.Header.Set("Authorization", "Bearer secret")
	commandResponse := httptest.NewRecorder()
	handler.ServeHTTP(commandResponse, commandRequest)
	if commandResponse.Code != http.StatusCreated {
		t.Fatalf("创建指令期望 201，实际为 %d", commandResponse.Code)
	}

	nextRequest := signedRequest(t, http.MethodGet, "/v1/commands/next", "", "device-a", "command-next-nonce", privateKey)
	nextResponse := httptest.NewRecorder()
	handler.ServeHTTP(nextResponse, nextRequest)
	if nextResponse.Code != http.StatusOK {
		t.Fatalf("拉取指令期望 200，实际为 %d", nextResponse.Code)
	}

	revokeRequest := httptest.NewRequest(http.MethodDelete, "/v1/devices/device-a", nil)
	revokeRequest.Header.Set("Authorization", "Bearer secret")
	revokeResponse := httptest.NewRecorder()
	handler.ServeHTTP(revokeResponse, revokeRequest)
	if revokeResponse.Code != http.StatusNoContent {
		t.Fatalf("撤销设备期望 204，实际为 %d", revokeResponse.Code)
	}

	afterRevoke := signedRequest(t, http.MethodGet, "/v1/commands/next", "", "device-a", "after-revoke-nonce", privateKey)
	afterRevokeResponse := httptest.NewRecorder()
	handler.ServeHTTP(afterRevokeResponse, afterRevoke)
	if afterRevokeResponse.Code != http.StatusUnauthorized {
		t.Fatalf("撤销后设备请求期望 401，实际为 %d", afterRevokeResponse.Code)
	}
}

func registerSigningDevice(t *testing.T, database *store.Store, deviceID string) *ecdsa.PrivateKey {
	t.Helper()
	privateKey, err := ecdsa.GenerateKey(elliptic.P256(), rand.Reader)
	if err != nil {
		t.Fatal(err)
	}
	der, err := x509.MarshalPKIXPublicKey(&privateKey.PublicKey)
	if err != nil {
		t.Fatal(err)
	}
	now := time.Now().UTC()
	if err := database.UpsertDevice(context.Background(), store.Device{
		ID:          deviceID,
		DisplayName: "测试手机",
		PublicKey:   base64.StdEncoding.EncodeToString(der),
		CreatedAt:   now,
		LastSeenAt:  now,
	}); err != nil {
		t.Fatal(err)
	}
	return privateKey
}

func signedRequest(
	t *testing.T,
	method string,
	target string,
	body string,
	deviceID string,
	nonce string,
	privateKey *ecdsa.PrivateKey,
) *http.Request {
	t.Helper()
	request := httptest.NewRequest(method, target, strings.NewReader(body))
	timestamp := time.Now().UTC().Unix()
	timestampText := strconv.FormatInt(timestamp, 10)
	canonical := canonicalRequest(request, timestampText, nonce, []byte(body))
	digest := sha256.Sum256(canonical)
	signature, err := ecdsa.SignASN1(rand.Reader, privateKey, digest[:])
	if err != nil {
		t.Fatal(err)
	}
	request.Header.Set("X-Device-Id", deviceID)
	request.Header.Set("X-Timestamp", timestampText)
	request.Header.Set("X-Nonce", nonce)
	request.Header.Set("X-Signature", base64.StdEncoding.EncodeToString(signature))
	return request
}
