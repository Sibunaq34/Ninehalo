USE nine_halo;

CREATE TABLE IF NOT EXISTS partner_comercial (
    id INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    nombre VARCHAR(120) NOT NULL,
    clave_hash BINARY(32) NOT NULL UNIQUE,
    activo BOOLEAN NOT NULL DEFAULT TRUE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS partner_pedido (
    id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    socio_id INT UNSIGNED NOT NULL,
    referencia VARCHAR(80) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    id_licencia INT UNSIGNED NOT NULL,
    cantidad SMALLINT UNSIGNED NOT NULL,
    precio_unitario DECIMAL(12,2) NOT NULL,
    estado ENUM('PENDIENTE','CONFIRMADO','CANCELADO') NOT NULL DEFAULT 'PENDIENTE',
    creado_utc DATETIME NOT NULL,
    confirmado_utc DATETIME NULL,
    vence_utc DATETIME NULL,
    UNIQUE KEY (socio_id, referencia),
    KEY (socio_id, estado, confirmado_utc),
    FOREIGN KEY (socio_id) REFERENCES partner_comercial(id)
) ENGINE=InnoDB;
