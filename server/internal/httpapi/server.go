package httpapi

import (
	"crypto/sha256"
	"crypto/subtle"
	"encoding/json"
	"errors"
	"io"
	"log/slog"
	"net"
	"net/http"
	"strings"
	"time"

	"github.com/September6969/MidnightVault/server/internal/store"
)

type server struct {
	store        *store.Store
	logger       *slog.Logger
	controlToken string
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
	mux.Handle("POST /v1/tasks/report", api.authorize(http.HandlerFunc(api.reportTask)))
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

	if strings.TrimSpace(device.ID) == "" || strings.TrimSpace(device.DisplayName) == "" || strings.TrimSpace(device.PublicKey) == "" {
		writeError(response, http.StatusBadRequest, "missing_device_fields")
		return
	}
	if device.CreatedAt.IsZero() {
		device.CreatedAt = time.Now().UTC()
	}

	if err := s.store.UpsertDevice(request.Context(), device); err != nil {
		s.logger.Error("保存设备失败", "error", err)
		writeError(response, http.StatusInternalServerError, "store_failed")
		return
	}
	writeJSON(response, http.StatusCreated, device)
}

func (s *server) reportTask(response http.ResponseWriter, request *http.Request) {
	var report store.TaskReport
	if err := decodeJSON(response, request, &report); err != nil {
		writeError(response, http.StatusBadRequest, "invalid_request")
		return
	}

	if strings.TrimSpace(report.TaskID) == "" || strings.TrimSpace(report.DeviceID) == "" || strings.TrimSpace(report.Status) == "" {
		writeError(response, http.StatusBadRequest, "missing_task_fields")
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
