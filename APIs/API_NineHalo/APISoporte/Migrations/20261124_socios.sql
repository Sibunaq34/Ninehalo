USE nine_halo;

CREATE TABLE IF NOT EXISTS socio_envio (
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    socio VARCHAR(20) NOT NULL,
    referencia VARCHAR(180) COLLATE utf8mb4_bin NOT NULL,
    contenido JSON NOT NULL,
    proximo_intento DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    entregado DATETIME NULL,
    UNIQUE KEY uq_socio_envio_referencia (socio, referencia),
    KEY ix_socio_envio_pendiente (socio, entregado, proximo_intento, id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS soporte_respuesta_socio (
    id_conversacion BIGINT UNSIGNED NOT NULL,
    referencia VARCHAR(80) COLLATE utf8mb4_bin NOT NULL,
    contenido TEXT NOT NULL,
    estado VARCHAR(20) NOT NULL,
    PRIMARY KEY (id_conversacion, referencia)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
