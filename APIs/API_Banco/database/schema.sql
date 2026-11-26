CREATE DATABASE IF NOT EXISTS banco_db CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE banco_db;

CREATE TABLE IF NOT EXISTS clientes (
  id INT AUTO_INCREMENT PRIMARY KEY,
  nombre VARCHAR(100) NOT NULL,
  cedula VARCHAR(20) NOT NULL UNIQUE
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS cuentas (
  id INT AUTO_INCREMENT PRIMARY KEY,
  cliente_id INT NOT NULL,
  saldo DECIMAL(15,2) NOT NULL,
  FOREIGN KEY (cliente_id) REFERENCES clientes(id)
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS tarjetas (
  id INT AUTO_INCREMENT PRIMARY KEY,
  cuenta_id INT NOT NULL,
  numero_tarjeta CHAR(16) NOT NULL UNIQUE,
  cvv VARCHAR(4) NOT NULL,
  fecha_vencimiento CHAR(5) NOT NULL,
  FOREIGN KEY (cuenta_id) REFERENCES cuentas(id)
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS sinpe_telefonos (
  id INT AUTO_INCREMENT PRIMARY KEY,
  cuenta_id INT NOT NULL,
  telefono CHAR(8) NOT NULL UNIQUE,
  FOREIGN KEY (cuenta_id) REFERENCES cuentas(id)
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS transacciones (
  id INT AUTO_INCREMENT PRIMARY KEY,
  cuenta_id INT NOT NULL,
  tipo ENUM('TARJETA','SINPE') NOT NULL,
  monto DECIMAL(15,2) NOT NULL,
  detalle VARCHAR(255) NOT NULL,
  fecha TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  FOREIGN KEY (cuenta_id) REFERENCES cuentas(id)
) ENGINE=InnoDB;

-- Ejecutar los datos de prueba una sola vez sobre una base recién creada.
INSERT INTO clientes (id, nombre, cedula) VALUES
  (1, 'Ana Rodríguez', '101110111'), (2, 'Luis Mora', '202220222'), (3, 'María Solís', '303330333');
INSERT INTO cuentas (id, cliente_id, saldo) VALUES (1,1,100000.00), (2,2,500.00), (3,3,25000.00);
INSERT INTO tarjetas (cuenta_id, numero_tarjeta, cvv, fecha_vencimiento) VALUES
  (1,'4111111111111111','123','12/39'),
  (2,'5555555555554444','456','12/39'),
  (3,'4000000000000002','789','01/20');
INSERT INTO sinpe_telefonos (cuenta_id, telefono) VALUES (1,'88881111'), (2,'87772222'), (3,'86663333');

DROP PROCEDURE IF EXISTS sp_obtener_tarjeta;
DROP PROCEDURE IF EXISTS sp_obtener_cuenta_por_telefono;
DROP PROCEDURE IF EXISTS sp_realizar_pago_tarjeta;
DROP PROCEDURE IF EXISTS sp_realizar_pago_sinpe;
DELIMITER $$
CREATE PROCEDURE sp_obtener_tarjeta(IN p_numero_tarjeta CHAR(16))
BEGIN
  SELECT t.id, t.cuenta_id, t.numero_tarjeta, t.cvv, t.fecha_vencimiento, c.saldo
  FROM tarjetas t JOIN cuentas c ON c.id = t.cuenta_id WHERE t.numero_tarjeta = p_numero_tarjeta;
END$$
CREATE PROCEDURE sp_obtener_cuenta_por_telefono(IN p_telefono CHAR(8))
BEGIN
  SELECT s.cuenta_id, s.telefono, c.saldo
  FROM sinpe_telefonos s JOIN cuentas c ON c.id = s.cuenta_id WHERE s.telefono = p_telefono;
END$$
-- La API administra la transacción, el bloqueo y todas las validaciones.
CREATE PROCEDURE sp_realizar_pago_tarjeta(IN p_cuenta_id INT, IN p_monto DECIMAL(15,2), IN p_detalle VARCHAR(255))
BEGIN
  UPDATE cuentas SET saldo = saldo - p_monto WHERE id = p_cuenta_id;
  INSERT INTO transacciones (cuenta_id, tipo, monto, detalle) VALUES (p_cuenta_id, 'TARJETA', p_monto, p_detalle);
  SELECT LAST_INSERT_ID() AS transaccionId, saldo AS nuevoSaldo FROM cuentas WHERE id = p_cuenta_id;
END$$
CREATE PROCEDURE sp_realizar_pago_sinpe(IN p_cuenta_id INT, IN p_monto DECIMAL(15,2), IN p_detalle VARCHAR(255))
BEGIN
  UPDATE cuentas SET saldo = saldo - p_monto WHERE id = p_cuenta_id;
  INSERT INTO transacciones (cuenta_id, tipo, monto, detalle) VALUES (p_cuenta_id, 'SINPE', p_monto, p_detalle);
  SELECT LAST_INSERT_ID() AS transaccionId, saldo AS nuevoSaldo FROM cuentas WHERE id = p_cuenta_id;
END$$
DELIMITER ;
