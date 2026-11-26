CREATE TABLE IF NOT EXISTS usuario_totp (
  id_usuario INT UNSIGNED NOT NULL,
  id_vinculacion VARCHAR(64) NOT NULL,
  secreto_protegido TEXT NOT NULL,
  habilitado BOOLEAN NOT NULL DEFAULT FALSE,
  pendiente_expira_en DATETIME(6) DEFAULT NULL,
  ultimo_paso BIGINT DEFAULT NULL,
  fallos INT UNSIGNED NOT NULL DEFAULT 0,
  bloqueado_hasta DATETIME(6) DEFAULT NULL,
  PRIMARY KEY (id_usuario),
  CONSTRAINT fk_usuario_totp_usuario FOREIGN KEY (id_usuario) REFERENCES usuario(id_usuario) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
