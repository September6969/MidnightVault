package main

import (
	"context"
	"flag"
	"log/slog"
	"net/http"
	"os"
	"os/signal"
	"path/filepath"
	"syscall"
	"time"

	"github.com/September6969/MidnightVault/server/internal/httpapi"
	"github.com/September6969/MidnightVault/server/internal/store"
)

func main() {
	address := flag.String("addr", "127.0.0.1:8090", "HTTP 监听地址")
	dataDirectory := flag.String("data", "./data", "SQLite 数据目录")
	flag.Parse()

	logger := slog.New(slog.NewJSONHandler(os.Stdout, nil))
	if err := os.MkdirAll(*dataDirectory, 0o700); err != nil {
		logger.Error("创建数据目录失败", "error", err)
		os.Exit(1)
	}

	database, err := store.Open(filepath.Join(*dataDirectory, "control.db"))
	if err != nil {
		logger.Error("打开数据库失败", "error", err)
		os.Exit(1)
	}
	defer database.Close()

	server := &http.Server{
		Addr:              *address,
		Handler:           httpapi.New(database, logger, os.Getenv("MIDNIGHTVAULT_CONTROL_TOKEN")),
		ReadHeaderTimeout: 10 * time.Second,
		ReadTimeout:       30 * time.Second,
		WriteTimeout:      30 * time.Second,
		IdleTimeout:       60 * time.Second,
	}

	stop := make(chan os.Signal, 1)
	signal.Notify(stop, syscall.SIGINT, syscall.SIGTERM)
	go func() {
		logger.Info("控制面服务已启动", "address", *address)
		if err := server.ListenAndServe(); err != nil && err != http.ErrServerClosed {
			logger.Error("HTTP 服务异常退出", "error", err)
			os.Exit(1)
		}
	}()

	<-stop
	shutdownContext, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	if err := server.Shutdown(shutdownContext); err != nil {
		logger.Error("服务停止超时", "error", err)
	}
}
