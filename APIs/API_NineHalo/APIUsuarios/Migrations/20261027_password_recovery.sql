CREATE TABLE IF NOT EXISTS usuario_recuperacion (
  id_usuario INT UNSIGNED NOT NULL PRIMARY KEY,
  fallos INT UNSIGNED NOT NULL DEFAULT 0,
  bloqueado_hasta DATETIME(6) NULL,
  nonce_hash VARCHAR(64) NULL,
  CONSTRAINT fk_recuperacion_usuario FOREIGN KEY(id_usuario) REFERENCES usuario(id_usuario) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
