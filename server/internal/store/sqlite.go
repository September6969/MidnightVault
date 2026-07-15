package store

import (
	"context"
	"database/sql"
	"fmt"
	"time"

	_ "modernc.org/sqlite"
)

type Store struct {
	db *sql.DB
}

type Device struct {
	ID          string    `json:"id"`
	DisplayName string    `json:"displayName"`
	PublicKey   string    `json:"publicKey"`
	CreatedAt   time.Time `json:"createdAt"`
}

type TaskReport struct {
	TaskID       string    `json:"taskId"`
	DeviceID     string    `json:"deviceId"`
	Status       string    `json:"status"`
	SuccessCount int       `json:"successCount"`
	SkippedCount int       `json:"skippedCount"`
	FailedCount  int       `json:"failedCount"`
	TotalBytes   int64     `json:"totalBytes"`
	ErrorCode    string    `json:"errorCode,omitempty"`
	CompletedAt  time.Time `json:"completedAt"`
}

func Open(path string) (*Store, error) {
	database, err := sql.Open("sqlite", path+"?_pragma=busy_timeout(5000)&_pragma=journal_mode(WAL)&_pragma=foreign_keys(1)")
	if err != nil {
		return nil, fmt.Errorf("打开 SQLite: %w", err)
	}

	database.SetMaxOpenConns(1)
	result := &Store{db: database}
	if err := result.migrate(context.Background()); err != nil {
		database.Close()
		return nil, err
	}

	return result, nil
}

func (s *Store) Close() error {
	return s.db.Close()
}

func (s *Store) Ping(ctx context.Context) error {
	return s.db.PingContext(ctx)
}

func (s *Store) UpsertDevice(ctx context.Context, device Device) error {
	_, err := s.db.ExecContext(ctx, `
		INSERT INTO devices (id, display_name, public_key, created_at)
		VALUES (?, ?, ?, ?)
		ON CONFLICT(id) DO UPDATE SET
			display_name = excluded.display_name,
			public_key = excluded.public_key`,
		device.ID,
		device.DisplayName,
		device.PublicKey,
		device.CreatedAt.UTC().Format(time.RFC3339Nano),
	)
	if err != nil {
		return fmt.Errorf("保存设备: %w", err)
	}
	return nil
}

func (s *Store) RecordTask(ctx context.Context, report TaskReport) error {
	_, err := s.db.ExecContext(ctx, `
		INSERT INTO task_reports (
			task_id, device_id, status, success_count, skipped_count,
			failed_count, total_bytes, error_code, completed_at
		) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
		ON CONFLICT(task_id) DO UPDATE SET
			status = excluded.status,
			success_count = excluded.success_count,
			skipped_count = excluded.skipped_count,
			failed_count = excluded.failed_count,
			total_bytes = excluded.total_bytes,
			error_code = excluded.error_code,
			completed_at = excluded.completed_at`,
		report.TaskID,
		report.DeviceID,
		report.Status,
		report.SuccessCount,
		report.SkippedCount,
		report.FailedCount,
		report.TotalBytes,
		report.ErrorCode,
		report.CompletedAt.UTC().Format(time.RFC3339Nano),
	)
	if err != nil {
		return fmt.Errorf("保存任务摘要: %w", err)
	}
	return nil
}

func (s *Store) migrate(ctx context.Context) error {
	const schema = `
		CREATE TABLE IF NOT EXISTS devices (
			id TEXT PRIMARY KEY,
			display_name TEXT NOT NULL,
			public_key TEXT NOT NULL,
			created_at TEXT NOT NULL
		);

		CREATE TABLE IF NOT EXISTS task_reports (
			task_id TEXT PRIMARY KEY,
			device_id TEXT NOT NULL,
			status TEXT NOT NULL,
			success_count INTEGER NOT NULL,
			skipped_count INTEGER NOT NULL,
			failed_count INTEGER NOT NULL,
			total_bytes INTEGER NOT NULL,
			error_code TEXT NOT NULL,
			completed_at TEXT NOT NULL,
			FOREIGN KEY(device_id) REFERENCES devices(id)
		);

		CREATE INDEX IF NOT EXISTS idx_task_reports_device_completed
		ON task_reports(device_id, completed_at DESC);
	`

	if _, err := s.db.ExecContext(ctx, schema); err != nil {
		return fmt.Errorf("执行数据库迁移: %w", err)
	}
	return nil
}
