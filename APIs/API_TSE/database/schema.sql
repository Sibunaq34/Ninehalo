CREATE DATABASE IF NOT EXISTS tse_db CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE tse_db;

CREATE TABLE IF NOT EXISTS personas (
  id INT AUTO_INCREMENT PRIMARY KEY,
  cedula VARCHAR(9) NOT NULL UNIQUE,
  nombre VARCHAR(100) NOT NULL,
  primer_apellido VARCHAR(100) NOT NULL,
  segundo_apellido VARCHAR(100) NOT NULL
) ENGINE=InnoDB;

-- Personas ficticias. Las tres primeras cédulas coinciden con las semillas del banco.
-- Al repetir el script se conservan los registros existentes.
INSERT INTO personas (cedula, nombre, primer_apellido, segundo_apellido) VALUES
('101110111', 'Ana', 'Rodríguez', 'Vargas'),
('202220222', 'Luis', 'Mora', 'Jiménez'),
('303330333', 'María', 'Solís', 'Castro'),
('102340567', 'José', 'Hernández', 'Rojas'),
('204560789', 'Laura', 'Chaves', 'Ramírez'),
('305670890', 'Carlos', 'Vargas', 'Mora'),
('406780901', 'Sofía', 'Jiménez', 'Alvarado'),
('507890123', 'Andrés', 'Campos', 'Sánchez')
ON DUPLICATE KEY UPDATE cedula = personas.cedula;

DROP PROCEDURE IF EXISTS sp_obtener_persona_por_cedula;
DELIMITER $$
CREATE PROCEDURE sp_obtener_persona_por_cedula(IN p_cedula VARCHAR(9))
BEGIN
  SELECT nombre, primer_apellido, segundo_apellido
  FROM personas WHERE cedula = p_cedula;
END$$
DELIMITER ;
