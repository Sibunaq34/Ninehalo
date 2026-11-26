-- En la base de Nine Halo. No altera compras ni facturas existentes.
CREATE TABLE IF NOT EXISTS compra_facturacion (
  id_usuario INT UNSIGNED NOT NULL,
  clave_operacion CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  contenido JSON NOT NULL,
  PRIMARY KEY(id_usuario,clave_operacion)
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS factura (
  id_factura BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
  numero VARCHAR(48) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  id_usuario INT UNSIGNED NOT NULL,
  clave_operacion CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  proveedor VARCHAR(10) NOT NULL,
  transaccion VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  metodo VARCHAR(10) NOT NULL,
  fecha_utc DATETIME NOT NULL,
  total_usd DECIMAL(15,2) NOT NULL,
  tipo_cambio DECIMAL(14,6) NOT NULL,
  total_crc DECIMAL(15,2) NOT NULL,
  contenido JSON NOT NULL,
  UNIQUE KEY uq_factura_numero(numero),
  UNIQUE KEY uq_factura_pedido(id_usuario,clave_operacion),
  UNIQUE KEY uq_factura_transaccion(proveedor,transaccion)
) ENGINE=InnoDB;
