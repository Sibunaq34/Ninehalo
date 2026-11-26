CREATE TABLE IF NOT EXISTS pedido_pago_revision (
 id_revision BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
 id_usuario INT UNSIGNED NOT NULL,
 clave_operacion CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 decision VARCHAR(40) NOT NULL,
 fuente VARCHAR(100) NOT NULL,
 evidencia JSON NOT NULL,
 fecha_utc DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
 KEY ix_revision_pedido(id_usuario,clave_operacion)
) ENGINE=InnoDB;
