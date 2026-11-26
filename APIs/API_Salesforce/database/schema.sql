CREATE DATABASE IF NOT EXISTS salesforce_db CHARACTER SET utf8mb4;
USE salesforce_db;
CREATE TABLE IF NOT EXISTS cases (id VARCHAR(40) PRIMARY KEY, compra VARCHAR(40) NOT NULL, asunto VARCHAR(180) NOT NULL, detalle TEXT NOT NULL, estado VARCHAR(20) NOT NULL DEFAULT 'ABIERTA', creado TIMESTAMP DEFAULT CURRENT_TIMESTAMP);
DELIMITER $$
DROP PROCEDURE IF EXISTS sp_salesforce_registrar_caso$$
CREATE PROCEDURE sp_salesforce_registrar_caso(IN p_id VARCHAR(40), IN p_compra VARCHAR(40), IN p_asunto VARCHAR(180), IN p_detalle TEXT)
BEGIN
  INSERT INTO cases(id,compra,asunto,detalle) VALUES(p_id,p_compra,p_asunto,p_detalle)
    ON DUPLICATE KEY UPDATE id=cases.id;
  SELECT * FROM cases WHERE id=p_id;
END$$

DROP PROCEDURE IF EXISTS sp_salesforce_listar_casos$$
CREATE PROCEDURE sp_salesforce_listar_casos()
BEGIN
  SELECT * FROM cases ORDER BY creado DESC LIMIT 100;
END$$

DROP PROCEDURE IF EXISTS sp_salesforce_consultar_caso$$
CREATE PROCEDURE sp_salesforce_consultar_caso(IN p_id VARCHAR(40))
BEGIN
  SELECT id FROM cases WHERE id=p_id;
END$$

DROP PROCEDURE IF EXISTS sp_salesforce_actualizar_caso$$
CREATE PROCEDURE sp_salesforce_actualizar_caso(IN p_id VARCHAR(40), IN p_estado VARCHAR(20))
BEGIN
  UPDATE cases SET estado=p_estado WHERE id=p_id AND (estado<>'RESUELTA' OR p_estado='RESUELTA');
END$$

DELIMITER ;

