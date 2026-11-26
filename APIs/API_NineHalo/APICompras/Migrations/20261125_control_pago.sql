CREATE TABLE IF NOT EXISTS pedido_pago_control (
 id_usuario INT UNSIGNED NOT NULL,
 clave_operacion CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 estado VARCHAR(20) NOT NULL,
 metodo VARCHAR(10) NULL,
 intento CHAR(36) NULL,
 codigo VARCHAR(50) NULL,
 respuesta JSON NULL,
 actualizado_utc DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
 PRIMARY KEY(id_usuario,clave_operacion)
) ENGINE=InnoDB;
-- Los pedidos anteriores no tienen una prueba fiable de ausencia de cobro.
INSERT IGNORE INTO pedido_pago_control(id_usuario,clave_operacion,estado)
SELECT id_usuario,LOWER(CONCAT(SUBSTRING(referencia_pago,LOCATE(':',referencia_pago,6)+1,8),'-',SUBSTRING(referencia_pago,LOCATE(':',referencia_pago,6)+9,4),'-',SUBSTRING(referencia_pago,LOCATE(':',referencia_pago,6)+13,4),'-',SUBSTRING(referencia_pago,LOCATE(':',referencia_pago,6)+17,4),'-',SUBSTRING(referencia_pago,LOCATE(':',referencia_pago,6)+21,12))),'LEGADO'
FROM compra WHERE estado='PENDIENTE' AND referencia_pago REGEXP '^cart:[0-9]+:[0-9a-f]{32}:' GROUP BY id_usuario,2;
