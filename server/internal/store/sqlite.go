package store

import (
	"context"
	"database/sql"
	"errors"
	"fmt"
	"time"

	_ "modernc.org/sqlite"
)

type Store struct {
	db *sql.DB
}

type Device struct {
	ID          string     `json:"id"`
	DisplayName string     `json:"displayName"`
	PublicKey   string     `json:"publicKey"`
	CreatedAt   time.Time  `json:"createdAt"`
	LastSeenAt  time.Time  `json:"lastSeenAt"`
	RevokedAt   *time.Time `json:"revokedAt,omitempty"`
}

type Command struct {
	ID             string     `json:"id"`
	DeviceID       string     `json:"deviceId"`
	Type           string     `json:"type"`
	Status         string     `json:"status"`
	CreatedAt      time.Time  `json:"createdAt"`
	ExpiresAt      time.Time  `json:"expiresAt"`
	AcknowledgedAt *time.Time `json:"acknowledgedAt,omitempty"`
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
	if device.LastSeenAt.IsZero() {
		device.LastSeenAt = device.CreatedAt
	}
	_, err := s.db.ExecContext(ctx, `
		INSERT INTO devices (id, display_name, public_key, created_at, last_seen_at, revoked_at)
		VALUES (?, ?, ?, ?, ?, NULL)
		ON CONFLICT(id) DO UPDATE SET
			display_name = excluded.display_name,
			public_key = excluded.public_key,
			last_seen_at = excluded.last_seen_at,
			revoked_at = NULL`,
		device.ID,
		device.DisplayName,
		device.PublicKey,
		device.CreatedAt.UTC().Format(time.RFC3339Nano),
		device.LastSeenAt.UTC().Format(time.RFC3339Nano),
	)
	if err != nil {
		return fmt.Errorf("保存设备: %w", err)
	}
	return nil
}

func (s *Store) GetDevice(ctx context.Context, id string) (Device, error) {
	row := s.db.QueryRowContext(ctx, `
		SELECT id, display_name, public_key, created_at, last_seen_at, revoked_at
		FROM devices WHERE id = ?`, id)
	return scanDevice(row)
}

func (s *Store) ListDevices(ctx context.Context) ([]Device, error) {
	rows, err := s.db.QueryContext(ctx, `
		SELECT id, display_name, public_key, created_at, last_seen_at, revoked_at
		FROM devices ORDER BY created_at`)
	if err != nil {
		return nil, fmt.Errorf("查询设备: %w", err)
	}
	defer rows.Close()

	devices := make([]Device, 0)
	for rows.Next() {
		device, scanErr := scanDevice(rows)
		if scanErr != nil {
			return nil, scanErr
		}
		devices = append(devices, device)
	}
	return devices, rows.Err()
}

func (s *Store) RevokeDevice(ctx context.Context, id string) (bool, error) {
	result, err := s.db.ExecContext(ctx, `
		UPDATE devices SET revoked_at = ? WHERE id = ? AND revoked_at IS NULL`,
		time.Now().UTC().Format(time.RFC3339Nano), id)
	if err != nil {
		return false, fmt.Errorf("撤销设备: %w", err)
	}
	count, err := result.RowsAffected()
	return count > 0, err
}

func (s *Store) TouchDevice(ctx context.Context, id string) error {
	_, err := s.db.ExecContext(ctx, `UPDATE devices SET last_seen_at = ? WHERE id = ?`,
		time.Now().UTC().Format(time.RFC3339Nano), id)
	return err
}

func (s *Store) UseNonce(ctx context.Context, deviceID, nonce string, expiresAt time.Time) (bool, error) {
	tx, err := s.db.BeginTx(ctx, nil)
	if err != nil {
		return false, fmt.Errorf("开始 nonce 事务: %w", err)
	}
	defer tx.Rollback()
	if _, err = tx.ExecContext(ctx, `DELETE FROM request_nonces WHERE expires_at <= ?`,
		time.Now().UTC().Format(time.RFC3339Nano)); err != nil {
		return false, fmt.Errorf("清理 nonce: %w", err)
	}
	if _, err = tx.ExecContext(ctx, `
		INSERT INTO request_nonces (device_id, nonce, expires_at) VALUES (?, ?, ?)`,
		deviceID, nonce, expiresAt.UTC().Format(time.RFC3339Nano)); err != nil {
		if isConstraintError(err) {
			return false, nil
		}
		return false, fmt.Errorf("保存 nonce: %w", err)
	}
	if err = tx.Commit(); err != nil {
		return false, fmt.Errorf("提交 nonce: %w", err)
	}
	return true, nil
}

func (s *Store) RecordTask(ctx context.Context, report TaskReport) error {
	tx, err := s.db.BeginTx(ctx, nil)
	if err != nil {
		return fmt.Errorf("开始任务摘要事务: %w", err)
	}
	defer tx.Rollback()
	if _, err = tx.ExecContext(ctx,
		`DELETE FROM task_reports WHERE completed_at < ?`,
		time.Now().UTC().AddDate(0, 0, -30).Format(time.RFC3339Nano)); err != nil {
		return fmt.Errorf("清理过期任务摘要: %w", err)
	}
	_, err = tx.ExecContext(ctx, `
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
	if err = tx.Commit(); err != nil {
		return fmt.Errorf("提交任务摘要: %w", err)
	}
	return nil
}

func (s *Store) ListTasks(ctx context.Context, deviceID string, limit int) ([]TaskReport, error) {
	limit = max(1, min(limit, 500))
	query := `
		SELECT task_id, device_id, status, success_count, skipped_count,
		       failed_count, total_bytes, error_code, completed_at
		FROM task_reports`
	args := []any{}
	if deviceID != "" {
		query += ` WHERE device_id = ?`
		args = append(args, deviceID)
	}
	query += ` ORDER BY completed_at DESC LIMIT ?`
	args = append(args, limit)
	rows, err := s.db.QueryContext(ctx, query, args...)
	if err != nil {
		return nil, fmt.Errorf("查询任务摘要: %w", err)
	}
	defer rows.Close()

	reports := make([]TaskReport, 0)
	for rows.Next() {
		var report TaskReport
		var completedAt string
		if err := rows.Scan(
			&report.TaskID,
			&report.DeviceID,
			&report.Status,
			&report.SuccessCount,
			&report.SkippedCount,
			&report.FailedCount,
			&report.TotalBytes,
			&report.ErrorCode,
			&completedAt); err != nil {
			return nil, fmt.Errorf("读取任务摘要: %w", err)
		}
		report.CompletedAt, err = time.Parse(time.RFC3339Nano, completedAt)
		if err != nil {
			return nil, fmt.Errorf("解析任务时间: %w", err)
		}
		reports = append(reports, report)
	}
	return reports, rows.Err()
}

func (s *Store) CreateCommand(ctx context.Context, command Command) error {
	_, err := s.db.ExecContext(ctx, `
		INSERT INTO commands (id, device_id, type, status, created_at, expires_at)
		VALUES (?, ?, ?, 'queued', ?, ?)`,
		command.ID,
		command.DeviceID,
		command.Type,
		command.CreatedAt.UTC().Format(time.RFC3339Nano),
		command.ExpiresAt.UTC().Format(time.RFC3339Nano))
	if err != nil {
		return fmt.Errorf("创建远程指令: %w", err)
	}
	return nil
}

func (s *Store) NextCommand(ctx context.Context, deviceID string) (*Command, error) {
	row := s.db.QueryRowContext(ctx, `
		SELECT id, device_id, type, status, created_at, expires_at, acknowledged_at
		FROM commands
		WHERE device_id = ? AND status = 'queued' AND expires_at > ?
		ORDER BY created_at LIMIT 1`,
		deviceID,
		time.Now().UTC().Format(time.RFC3339Nano))
	command, err := scanCommand(row)
	if errors.Is(err, sql.ErrNoRows) {
		return nil, nil
	}
	if err != nil {
		return nil, err
	}
	return &command, nil
}

func (s *Store) AcknowledgeCommand(ctx context.Context, deviceID, commandID string) (bool, error) {
	result, err := s.db.ExecContext(ctx, `
		UPDATE commands
		SET status = 'acknowledged', acknowledged_at = ?
		WHERE id = ? AND device_id = ? AND status = 'queued'`,
		time.Now().UTC().Format(time.RFC3339Nano), commandID, deviceID)
	if err != nil {
		return false, fmt.Errorf("确认远程指令: %w", err)
	}
	count, err := result.RowsAffected()
	return count > 0, err
}

func (s *Store) migrate(ctx context.Context) error {
	const schema = `
		CREATE TABLE IF NOT EXISTS devices (
			id TEXT PRIMARY KEY,
			display_name TEXT NOT NULL,
			public_key TEXT NOT NULL,
			created_at TEXT NOT NULL,
			last_seen_at TEXT NOT NULL,
			revoked_at TEXT
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

		CREATE TABLE IF NOT EXISTS request_nonces (
			device_id TEXT NOT NULL,
			nonce TEXT NOT NULL,
			expires_at TEXT NOT NULL,
			PRIMARY KEY(device_id, nonce),
			FOREIGN KEY(device_id) REFERENCES devices(id)
		);

		CREATE TABLE IF NOT EXISTS commands (
			id TEXT PRIMARY KEY,
			device_id TEXT NOT NULL,
			type TEXT NOT NULL,
			status TEXT NOT NULL,
			created_at TEXT NOT NULL,
			expires_at TEXT NOT NULL,
			acknowledged_at TEXT,
			FOREIGN KEY(device_id) REFERENCES devices(id)
		);

		CREATE INDEX IF NOT EXISTS idx_commands_device_status
		ON commands(device_id, status, created_at);
	`

	if _, err := s.db.ExecContext(ctx, schema); err != nil {
		return fmt.Errorf("执行数据库迁移: %w", err)
	}
	if err := s.ensureColumn(ctx, "devices", "last_seen_at", "TEXT NOT NULL DEFAULT '1970-01-01T00:00:00Z'"); err != nil {
		return err
	}
	if err := s.ensureColumn(ctx, "devices", "revoked_at", "TEXT"); err != nil {
		return err
	}
	return nil
}

func (s *Store) ensureColumn(ctx context.Context, table, column, declaration string) error {
	rows, err := s.db.QueryContext(ctx, "PRAGMA table_info("+table+")")
	if err != nil {
		return fmt.Errorf("读取表结构: %w", err)
	}
	found := false
	for rows.Next() {
		var index int
		var name, kind string
		var notNull, primaryKey int
		var defaultValue sql.NullString
		if err := rows.Scan(&index, &name, &kind, &notNull, &defaultValue, &primaryKey); err != nil {
			rows.Close()
			return fmt.Errorf("读取列结构: %w", err)
		}
		if name == column {
			found = true
		}
	}
	if err := rows.Close(); err != nil {
		return err
	}
	if found {
		return nil
	}
	if _, err := s.db.ExecContext(ctx, "ALTER TABLE "+table+" ADD COLUMN "+column+" "+declaration); err != nil {
		return fmt.Errorf("添加数据库列 %s.%s: %w", table, column, err)
	}
	return nil
}

type rowScanner interface {
	Scan(dest ...any) error
}

func scanDevice(row rowScanner) (Device, error) {
	var device Device
	var createdAt, lastSeenAt string
	var revokedAt sql.NullString
	if err := row.Scan(
		&device.ID,
		&device.DisplayName,
		&device.PublicKey,
		&createdAt,
		&lastSeenAt,
		&revokedAt); err != nil {
		return Device{}, err
	}
	var err error
	device.CreatedAt, err = time.Parse(time.RFC3339Nano, createdAt)
	if err != nil {
		return Device{}, fmt.Errorf("解析设备创建时间: %w", err)
	}
	device.LastSeenAt, err = time.Parse(time.RFC3339Nano, lastSeenAt)
	if err != nil {
		return Device{}, fmt.Errorf("解析设备在线时间: %w", err)
	}
	if revokedAt.Valid {
		value, parseErr := time.Parse(time.RFC3339Nano, revokedAt.String)
		if parseErr != nil {
			return Device{}, fmt.Errorf("解析设备撤销时间: %w", parseErr)
		}
		device.RevokedAt = &value
	}
	return device, nil
}

func scanCommand(row rowScanner) (Command, error) {
	var command Command
	var createdAt, expiresAt string
	var acknowledgedAt sql.NullString
	if err := row.Scan(
		&command.ID,
		&command.DeviceID,
		&command.Type,
		&command.Status,
		&createdAt,
		&expiresAt,
		&acknowledgedAt); err != nil {
		return Command{}, err
	}
	var err error
	command.CreatedAt, err = time.Parse(time.RFC3339Nano, createdAt)
	if err != nil {
		return Command{}, err
	}
	command.ExpiresAt, err = time.Parse(time.RFC3339Nano, expiresAt)
	if err != nil {
		return Command{}, err
	}
	if acknowledgedAt.Valid {
		value, parseErr := time.Parse(time.RFC3339Nano, acknowledgedAt.String)
		if parseErr != nil {
			return Command{}, parseErr
		}
		command.AcknowledgedAt = &value
	}
	return command, nil
}

func isConstraintError(err error) bool {
	return err != nil && (errors.Is(err, sql.ErrNoRows) ||
		contains(err.Error(), "constraint"))
}

func contains(value, fragment string) bool {
	for index := 0; index+len(fragment) <= len(value); index++ {
		if value[index:index+len(fragment)] == fragment {
			return true
		}
	}
	return false
}
