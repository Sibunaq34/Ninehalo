CREATE DATABASE IF NOT EXISTS contacloud_db CHARACTER SET utf8mb4;
USE contacloud_db;
CREATE TABLE IF NOT EXISTS ventas (factura VARCHAR(180) PRIMARY KEY, fecha VARCHAR(40) NOT NULL, total_usd DECIMAL(16,2) NOT NULL, total_crc DECIMAL(16,2) NOT NULL);
DELIMITER $$
DROP PROCEDURE IF EXISTS sp_contacloud_registrar_venta$$
CREATE PROCEDURE sp_contacloud_registrar_venta(IN p_factura VARCHAR(180), IN p_fecha VARCHAR(40), IN p_usd DECIMAL(16,2), IN p_crc DECIMAL(16,2))
BEGIN
  INSERT INTO ventas(factura,fecha,total_usd,total_crc) VALUES(p_factura,p_fecha,p_usd,p_crc)
    ON DUPLICATE KEY UPDATE factura=ventas.factura;
  SELECT * FROM ventas WHERE factura=p_factura;
END$$

DROP PROCEDURE IF EXISTS sp_contacloud_listar_ventas$$
CREATE PROCEDURE sp_contacloud_listar_ventas()
BEGIN
  SELECT * FROM ventas ORDER BY fecha DESC LIMIT 100;
END$$

DROP PROCEDURE IF EXISTS sp_contacloud_resumen$$
CREATE PROCEDURE sp_contacloud_resumen()
BEGIN
  SELECT SUBSTR(fecha,1,7) AS mes, COUNT(*) AS ventas,
         ROUND(SUM(total_usd),2) AS total_usd, ROUND(SUM(total_crc),2) AS total_crc
  FROM ventas GROUP BY mes ORDER BY mes DESC;
END$$

DELIMITER ;

