CREATE DATABASE IF NOT EXISTS notifyhub_db CHARACTER SET utf8mb4;
USE notifyhub_db;
CREATE TABLE IF NOT EXISTS avisos (referencia VARCHAR(180) PRIMARY KEY, destinatario VARCHAR(180) NOT NULL, mensaje TEXT NOT NULL, estado VARCHAR(20) NOT NULL DEFAULT 'ENVIADA', creado TIMESTAMP DEFAULT CURRENT_TIMESTAMP);
DELIMITER $$
DROP PROCEDURE IF EXISTS sp_notifyhub_registrar_aviso$$
CREATE PROCEDURE sp_notifyhub_registrar_aviso(IN p_ref VARCHAR(180), IN p_destinatario VARCHAR(180), IN p_mensaje TEXT)
BEGIN
  INSERT INTO avisos(referencia,destinatario,mensaje) VALUES(p_ref,p_destinatario,p_mensaje)
    ON DUPLICATE KEY UPDATE referencia=avisos.referencia;
  SELECT * FROM avisos WHERE referencia=p_ref;
END$$

DROP PROCEDURE IF EXISTS sp_notifyhub_listar_avisos$$
CREATE PROCEDURE sp_notifyhub_listar_avisos()
BEGIN
  SELECT * FROM avisos ORDER BY creado DESC LIMIT 100;
END$$

DELIMITER ;

