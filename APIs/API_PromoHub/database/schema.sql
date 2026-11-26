CREATE DATABASE IF NOT EXISTS promohub_db CHARACTER SET utf8mb4;
USE promohub_db;

CREATE TABLE IF NOT EXISTS cupon (
  codigo VARCHAR(32) PRIMARY KEY,
  porcentaje DECIMAL(5,2) NOT NULL CHECK (porcentaje > 0 AND porcentaje < 100),
  vence_utc DATETIME NOT NULL,
  limite_usos INT UNSIGNED NOT NULL,
  licencias JSON NULL,
  activo BOOLEAN NOT NULL DEFAULT TRUE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS cupon_pedido (
  id_usuario INT UNSIGNED NOT NULL,
  clave_operacion CHAR(36) NOT NULL,
  codigo VARCHAR(32) NOT NULL,
  porcentaje DECIMAL(5,2) NOT NULL,
  confirmado_utc DATETIME NULL,
  PRIMARY KEY (id_usuario, clave_operacion),
  KEY (codigo),
  FOREIGN KEY (codigo) REFERENCES cupon(codigo)
) ENGINE=InnoDB;

INSERT INTO cupon(codigo, porcentaje, vence_utc, limite_usos, licencias)
VALUES ('NINE10', 10, DATE_ADD(UTC_TIMESTAMP(), INTERVAL 1 YEAR), 100, JSON_ARRAY(2,3))
ON DUPLICATE KEY UPDATE codigo=cupon.codigo;

DELIMITER $$
CREATE PROCEDURE IF NOT EXISTS sp_promohub_validar(IN p_codigo VARCHAR(32), IN p_licencias JSON)
BEGIN
  SELECT c.codigo, c.porcentaje
  FROM cupon c
  WHERE c.codigo=p_codigo AND c.activo AND c.vence_utc>UTC_TIMESTAMP()
    AND (c.licencias IS NULL OR JSON_CONTAINS(c.licencias,p_licencias))
    AND (SELECT COUNT(*) FROM cupon_pedido p WHERE p.codigo=c.codigo)<c.limite_usos;
END$$

CREATE PROCEDURE IF NOT EXISTS sp_promohub_reservar(
  IN p_codigo VARCHAR(32), IN p_licencias JSON, IN p_usuario INT UNSIGNED, IN p_operacion CHAR(36))
BEGIN
  DECLARE v_porcentaje DECIMAL(5,2);
  DECLARE v_limite INT UNSIGNED;
  DECLARE v_usos INT UNSIGNED;
  SELECT porcentaje, limite_usos INTO v_porcentaje, v_limite
  FROM cupon WHERE codigo=p_codigo AND activo AND vence_utc>UTC_TIMESTAMP()
    AND (licencias IS NULL OR JSON_CONTAINS(licencias,p_licencias))
  FOR UPDATE;
  IF v_porcentaje IS NOT NULL THEN
    SELECT COUNT(*) INTO v_usos FROM cupon_pedido WHERE codigo=p_codigo FOR UPDATE;
  END IF;
  IF v_usos<v_limite THEN
    INSERT INTO cupon_pedido(id_usuario,clave_operacion,codigo,porcentaje)
    VALUES(p_usuario,p_operacion,p_codigo,v_porcentaje);
    SELECT v_porcentaje AS porcentaje;
  END IF;
END$$

CREATE PROCEDURE IF NOT EXISTS sp_promohub_confirmar(IN p_usuario INT UNSIGNED, IN p_operacion CHAR(36))
BEGIN
  UPDATE cupon_pedido SET confirmado_utc=UTC_TIMESTAMP()
  WHERE id_usuario=p_usuario AND clave_operacion=p_operacion AND confirmado_utc IS NULL;
END$$
DELIMITER ;
