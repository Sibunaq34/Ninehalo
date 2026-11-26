-- Ejecutar en la misma base de APICompras. No altera compras existentes.
CREATE TABLE IF NOT EXISTS checkout_paypal (
  id_usuario INT NOT NULL,
  clave_operacion CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  monto DECIMAL(15,2) NOT NULL,
  moneda CHAR(3) NOT NULL,
  entorno VARCHAR(10) NOT NULL,
  paypal_order_id VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NULL,
  capture_id VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NULL,
  creado_utc DATETIME NOT NULL,
  PRIMARY KEY (id_usuario, clave_operacion),
  UNIQUE KEY uq_paypal_order (paypal_order_id),
  UNIQUE KEY uq_paypal_capture (capture_id)
) ENGINE=InnoDB;
