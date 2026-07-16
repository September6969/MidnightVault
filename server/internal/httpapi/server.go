package httpapi

import (
	"bytes"
	"context"
	"crypto/ecdsa"
	"crypto/rand"
	"crypto/sha256"
	"crypto/subtle"
	"crypto/x509"
	"encoding/base64"
	"encoding/hex"
	"encoding/json"
	"errors"
	"io"
	"log/slog"
	"net"
	"net/http"
	"strconv"
	"strings"
	"time"

	"github.com/September6969/MidnightVault/server/internal/store"
)

type server struct {
	store        *store.Store
	logger       *slog.Logger
	controlToken string
}

type deviceContextKey struct{}

type createCommandRequest struct {
	DeviceID string `json:"deviceId"`
	Type     string `json:"type"`
}

var validTaskStatuses = map[string]struct{}{
	"queued":    {},
	"running":   {},
	"paused":    {},
	"completed": {},
	"failed":    {},
}

func New(database *store.Store, logger *slog.Logger, controlToken string) http.Handler {
	api := &server{store: database, logger: logger, controlToken: controlToken}
	mux := http.NewServeMux()
	mux.HandleFunc("GET /healthz", api.health)
	mux.Handle("POST /v1/devices", api.authorize(http.HandlerFunc(api.registerDevice)))
	mux.Handle("GET /v1/devices", api.authorize(http.HandlerFunc(api.listDevices)))
	mux.Handle("DELETE /v1/devices/{id}", api.authorize(http.HandlerFunc(api.revokeDevice)))
	mux.Handle("GET /v1/tasks", api.authorize(http.HandlerFunc(api.listTasks)))
	mux.Handle("POST /v1/commands", api.authorize(http.HandlerFunc(api.createCommand)))
	mux.Handle("POST /v1/tasks/report", api.authorizeDevice(http.HandlerFunc(api.reportTask)))
	mux.Handle("GET /v1/commands/next", api.authorizeDevice(http.HandlerFunc(api.nextCommand)))
	mux.Handle("POST /v1/commands/{id}/ack", api.authorizeDevice(http.HandlerFunc(api.acknowledgeCommand)))
	return requestLogging(logger, mux)
}

func (s *server) health(response http.ResponseWriter, request *http.Request) {
	if err := s.store.Ping(request.Context()); err != nil {
		writeError(response, http.StatusServiceUnavailable, "database_unavailable")
		return
	}

	writeJSON(response, http.StatusOK, map[string]any{
		"status":  "ok",
		"service": "MidnightVault.ControlPlane",
		"utc":     time.Now().UTC(),
	})
}

func (s *server) registerDevice(response http.ResponseWriter, request *http.Request) {
	var device store.Device
	if err := decodeJSON(response, request, &device); err != nil {
		writeError(response, http.StatusBadRequest, "invalid_request")
		return
	}

	device.ID = strings.TrimSpace(device.ID)
	device.DisplayName = strings.TrimSpace(device.DisplayName)
	device.PublicKey = strings.TrimSpace(device.PublicKey)
	if device.ID == "" || device.DisplayName == "" || device.PublicKey == "" || !validPublicKey(device.PublicKey) {
		writeError(response, http.StatusBadRequest, "invalid_device_fields")
		return
	}
	if device.CreatedAt.IsZero() {
		device.CreatedAt = time.Now().UTC()
	}
	device.LastSeenAt = time.Now().UTC()

	if err := s.store.UpsertDevice(request.Context(), device); err != nil {
		s.logger.Error("保存设备失败", "error", err)
		writeError(response, http.StatusInternalServerError, "store_failed")
		return
	}
	writeJSON(response, http.StatusCreated, device)
}

func (s *server) listDevices(response http.ResponseWriter, request *http.Request) {
	devices, err := s.store.ListDevices(request.Context())
	if err != nil {
		s.logger.Error("查询设备失败", "error", err)
		writeError(response, http.StatusInternalServerError, "store_failed")
		return
	}
	writeJSON(response, http.StatusOK, devices)
}

func (s *server) revokeDevice(response http.ResponseWriter, request *http.Request) {
	id := strings.TrimSpace(request.PathValue("id"))
	if id == "" {
		writeError(response, http.StatusBadRequest, "missing_device_id")
		return
	}
	revoked, err := s.store.RevokeDevice(request.Context(), id)
	if err != nil {
		s.logger.Error("撤销设备失败", "error", err)
		writeError(response, http.StatusInternalServerError, "store_failed")
		return
	}
	if !revoked {
		writeError(response, http.StatusNotFound, "device_not_found_or_revoked")
		return
	}
	response.WriteHeader(http.StatusNoContent)
}

func (s *server) reportTask(response http.ResponseWriter, request *http.Request) {
	deviceID := deviceIDFromContext(request.Context())
	var report store.TaskReport
	if err := decodeJSON(response, request, &report); err != nil {
		writeError(response, http.StatusBadRequest, "invalid_request")
		return
	}

	if strings.TrimSpace(report.TaskID) == "" || report.DeviceID != deviceID || strings.TrimSpace(report.Status) == "" {
		writeError(response, http.StatusBadRequest, "missing_or_mismatched_task_fields")
		return
	}
	report.Status = strings.TrimSpace(report.Status)
	if _, valid := validTaskStatuses[report.Status]; !valid ||
		report.SuccessCount < 0 || report.SkippedCount < 0 || report.FailedCount < 0 || report.TotalBytes < 0 {
		writeError(response, http.StatusBadRequest, "invalid_task_summary")
		return
	}
	if report.CompletedAt.IsZero() {
		report.CompletedAt = time.Now().UTC()
	}

	if err := s.store.RecordTask(request.Context(), report); err != nil {
		s.logger.Error("保存任务摘要失败", "error", err)
		writeError(response, http.StatusInternalServerError, "store_failed")
		return
	}
	_ = s.store.TouchDevice(request.Context(), deviceID)
	response.WriteHeader(http.StatusNoContent)
}

func (s *server) listTasks(response http.ResponseWriter, request *http.Request) {
	limit := 100
	if rawLimit := request.URL.Query().Get("limit"); rawLimit != "" {
		parsed, err := strconv.Atoi(rawLimit)
		if err != nil || parsed < 1 || parsed > 500 {
			writeError(response, http.StatusBadRequest, "invalid_limit")
			return
		}
		limit = parsed
	}
	reports, err := s.store.ListTasks(
		request.Context(),
		strings.TrimSpace(request.URL.Query().Get("deviceId")),
		limit)
	if err != nil {
		s.logger.Error("查询任务摘要失败", "error", err)
		writeError(response, http.StatusInternalServerError, "store_failed")
		return
	}
	writeJSON(response, http.StatusOK, reports)
}

func (s *server) createCommand(response http.ResponseWriter, request *http.Request) {
	var input createCommandRequest
	if err := decodeJSON(response, request, &input); err != nil {
		writeError(response, http.StatusBadRequest, "invalid_request")
		return
	}
	input.DeviceID = strings.TrimSpace(input.DeviceID)
	input.Type = strings.TrimSpace(input.Type)
	if input.DeviceID == "" || input.Type != "backup-now" {
		writeError(response, http.StatusBadRequest, "invalid_command")
		return
	}
	device, err := s.store.GetDevice(request.Context(), input.DeviceID)
	if err != nil || device.RevokedAt != nil {
		writeError(response, http.StatusNotFound, "active_device_not_found")
		return
	}
	command := store.Command{
		ID:        randomID(),
		DeviceID:  input.DeviceID,
		Type:      input.Type,
		Status:    "queued",
		CreatedAt: time.Now().UTC(),
		ExpiresAt: time.Now().UTC().Add(24 * time.Hour),
	}
	if err := s.store.CreateCommand(request.Context(), command); err != nil {
		s.logger.Error("创建远程指令失败", "error", err)
		writeError(response, http.StatusInternalServerError, "store_failed")
		return
	}
	writeJSON(response, http.StatusCreated, command)
}

func (s *server) nextCommand(response http.ResponseWriter, request *http.Request) {
	deviceID := deviceIDFromContext(request.Context())
	command, err := s.store.NextCommand(request.Context(), deviceID)
	if err != nil {
		s.logger.Error("查询远程指令失败", "error", err)
		writeError(response, http.StatusInternalServerError, "store_failed")
		return
	}
	_ = s.store.TouchDevice(request.Context(), deviceID)
	if command == nil {
		response.WriteHeader(http.StatusNoContent)
		return
	}
	writeJSON(response, http.StatusOK, command)
}

func (s *server) acknowledgeCommand(response http.ResponseWriter, request *http.Request) {
	acknowledged, err := s.store.AcknowledgeCommand(
		request.Context(),
		deviceIDFromContext(request.Context()),
		request.PathValue("id"))
	if err != nil {
		s.logger.Error("确认远程指令失败", "error", err)
		writeError(response, http.StatusInternalServerError, "store_failed")
		return
	}
	if !acknowledged {
		writeError(response, http.StatusNotFound, "command_not_found")
		return
	}
	response.WriteHeader(http.StatusNoContent)
}

func (s *server) authorize(next http.Handler) http.Handler {
	return http.HandlerFunc(func(response http.ResponseWriter, request *http.Request) {
		if s.controlToken == "" {
			if isLoopback(request.RemoteAddr) {
				next.ServeHTTP(response, request)
				return
			}
			writeError(response, http.StatusUnauthorized, "unauthorized")
			return
		}

		const prefix = "Bearer "
		authorization := request.Header.Get("Authorization")
		if !strings.HasPrefix(authorization, prefix) || !equalSecret(s.controlToken, strings.TrimPrefix(authorization, prefix)) {
			writeError(response, http.StatusUnauthorized, "unauthorized")
			return
		}
		next.ServeHTTP(response, request)
	})
}

func (s *server) authorizeDevice(next http.Handler) http.Handler {
	return http.HandlerFunc(func(response http.ResponseWriter, request *http.Request) {
		deviceID := strings.TrimSpace(request.Header.Get("X-Device-Id"))
		timestampText := request.Header.Get("X-Timestamp")
		nonce := request.Header.Get("X-Nonce")
		signatureText := request.Header.Get("X-Signature")
		if deviceID == "" || len(nonce) < 16 || len(nonce) > 128 || timestampText == "" || signatureText == "" {
			writeError(response, http.StatusUnauthorized, "missing_signature_headers")
			return
		}
		timestamp, err := strconv.ParseInt(timestampText, 10, 64)
		if err != nil || timestamp < time.Now().Add(-5*time.Minute).Unix() || timestamp > time.Now().Add(5*time.Minute).Unix() {
			writeError(response, http.StatusUnauthorized, "invalid_timestamp")
			return
		}

		device, err := s.store.GetDevice(request.Context(), deviceID)
		if err != nil || device.RevokedAt != nil {
			writeError(response, http.StatusUnauthorized, "unknown_or_revoked_device")
			return
		}
		body, err := readRequestBody(request)
		if err != nil {
			writeError(response, http.StatusRequestEntityTooLarge, "request_too_large")
			return
		}
		canonical := canonicalRequest(request, timestampText, nonce, body)
		if !verifyDeviceSignature(device.PublicKey, signatureText, canonical) {
			writeError(response, http.StatusUnauthorized, "invalid_signature")
			return
		}
		used, err := s.store.UseNonce(
			request.Context(),
			deviceID,
			nonce,
			time.Unix(timestamp, 0).UTC().Add(10*time.Minute))
		if err != nil {
			s.logger.Error("保存防重放 nonce 失败", "error", err)
			writeError(response, http.StatusInternalServerError, "store_failed")
			return
		}
		if !used {
			writeError(response, http.StatusConflict, "replayed_request")
			return
		}

		ctx := context.WithValue(request.Context(), deviceContextKey{}, deviceID)
		next.ServeHTTP(response, request.WithContext(ctx))
	})
}

func decodeJSON(response http.ResponseWriter, request *http.Request, destination any) error {
	request.Body = http.MaxBytesReader(response, request.Body, 1024*1024)
	decoder := json.NewDecoder(request.Body)
	decoder.DisallowUnknownFields()
	if err := decoder.Decode(destination); err != nil {
		return err
	}
	if err := decoder.Decode(&struct{}{}); !errors.Is(err, io.EOF) {
		return errors.New("请求体只能包含一个 JSON 对象")
	}
	return nil
}

func readRequestBody(request *http.Request) ([]byte, error) {
	if request.Body == nil {
		return nil, nil
	}
	body, err := io.ReadAll(io.LimitReader(request.Body, 1024*1024+1))
	if err != nil {
		return nil, err
	}
	if len(body) > 1024*1024 {
		return nil, errors.New("请求体过大")
	}
	request.Body = io.NopCloser(bytes.NewReader(body))
	return body, nil
}

func canonicalRequest(request *http.Request, timestamp, nonce string, body []byte) []byte {
	bodyHash := sha256.Sum256(body)
	value := request.Method + "\n" + request.URL.RequestURI() + "\n" + timestamp + "\n" + nonce + "\n" + hex.EncodeToString(bodyHash[:])
	return []byte(value)
}

func verifyDeviceSignature(publicKeyText, signatureText string, canonical []byte) bool {
	der, err := base64.StdEncoding.DecodeString(publicKeyText)
	if err != nil {
		return false
	}
	parsed, err := x509.ParsePKIXPublicKey(der)
	if err != nil {
		return false
	}
	publicKey, ok := parsed.(*ecdsa.PublicKey)
	if !ok {
		return false
	}
	signature, err := base64.StdEncoding.DecodeString(signatureText)
	if err != nil {
		return false
	}
	digest := sha256.Sum256(canonical)
	return ecdsa.VerifyASN1(publicKey, digest[:], signature)
}

func validPublicKey(value string) bool {
	der, err := base64.StdEncoding.DecodeString(value)
	if err != nil {
		return false
	}
	parsed, err := x509.ParsePKIXPublicKey(der)
	if err != nil {
		return false
	}
	_, ok := parsed.(*ecdsa.PublicKey)
	return ok
}

func deviceIDFromContext(ctx context.Context) string {
	value, _ := ctx.Value(deviceContextKey{}).(string)
	return value
}

func randomID() string {
	value := make([]byte, 16)
	if _, err := rand.Read(value); err != nil {
		panic("系统随机数不可用: " + err.Error())
	}
	return hex.EncodeToString(value)
}

func writeJSON(response http.ResponseWriter, status int, value any) {
	response.Header().Set("Content-Type", "application/json; charset=utf-8")
	response.WriteHeader(status)
	_ = json.NewEncoder(response).Encode(value)
}

func writeError(response http.ResponseWriter, status int, code string) {
	writeJSON(response, status, map[string]string{"error": code})
}

func equalSecret(expected, actual string) bool {
	expectedHash := sha256.Sum256([]byte(expected))
	actualHash := sha256.Sum256([]byte(actual))
	return subtle.ConstantTimeCompare(expectedHash[:], actualHash[:]) == 1
}

func isLoopback(remoteAddress string) bool {
	host, _, err := net.SplitHostPort(remoteAddress)
	if err != nil {
		return false
	}
	address := net.ParseIP(host)
	return address != nil && address.IsLoopback()
}

func requestLogging(logger *slog.Logger, next http.Handler) http.Handler {
	return http.HandlerFunc(func(response http.ResponseWriter, request *http.Request) {
		started := time.Now()
		next.ServeHTTP(response, request)
		logger.Info("HTTP 请求完成",
			"method", request.Method,
			"path", request.URL.Path,
			"duration", time.Since(started),
		)
	})
}
