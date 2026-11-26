-- Ejecutar una vez en una base existente para admitir CVV American Express.
USE banco_db;
ALTER TABLE tarjetas MODIFY cvv VARCHAR(4) NOT NULL;
