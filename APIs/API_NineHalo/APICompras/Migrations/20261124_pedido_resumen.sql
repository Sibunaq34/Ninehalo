-- Conserva precios de catálogo y totales definitivos del pedido, también al reintentar.
CREATE TABLE IF NOT EXISTS pedido_resumen (
 id_usuario INT UNSIGNED NOT NULL,
 clave_operacion CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 contenido JSON NOT NULL,
 PRIMARY KEY(id_usuario,clave_operacion)
) ENGINE=InnoDB;
