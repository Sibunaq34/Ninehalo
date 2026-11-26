-- MySQL 8.0.16+. Ejecutar una vez sobre la base nine_halo existente.
-- No incluye un USE para permitir validar en una base de pruebas.
-- Los catalogos se cargan por separado; no se inventan datos geograficos.

CREATE TABLE pais (
  id_pais INT UNSIGNED NOT NULL AUTO_INCREMENT,
  descripcion VARCHAR(100) NOT NULL,
  PRIMARY KEY (id_pais),
  UNIQUE KEY uq_pais_descripcion (descripcion),
  CONSTRAINT ck_pais_descripcion CHECK (CHAR_LENGTH(TRIM(descripcion)) > 0 AND OCTET_LENGTH(descripcion) = OCTET_LENGTH(TRIM(descripcion)))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE provincias (
  id_provincia INT UNSIGNED NOT NULL AUTO_INCREMENT,
  id_pais INT UNSIGNED NOT NULL,
  descripcion VARCHAR(100) NOT NULL,
  PRIMARY KEY (id_provincia),
  UNIQUE KEY uq_provincia_pais_descripcion (id_pais, descripcion),
  CONSTRAINT fk_provincia_pais FOREIGN KEY (id_pais) REFERENCES pais(id_pais) ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT ck_provincia_descripcion CHECK (CHAR_LENGTH(TRIM(descripcion)) > 0 AND OCTET_LENGTH(descripcion) = OCTET_LENGTH(TRIM(descripcion)))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE cantones (
  id_canton INT UNSIGNED NOT NULL AUTO_INCREMENT,
  id_provincia INT UNSIGNED NOT NULL,
  descripcion VARCHAR(100) NOT NULL,
  PRIMARY KEY (id_canton),
  UNIQUE KEY uq_canton_provincia_descripcion (id_provincia, descripcion),
  CONSTRAINT fk_canton_provincia FOREIGN KEY (id_provincia) REFERENCES provincias(id_provincia) ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT ck_canton_descripcion CHECK (CHAR_LENGTH(TRIM(descripcion)) > 0 AND OCTET_LENGTH(descripcion) = OCTET_LENGTH(TRIM(descripcion)))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE distritos (
  id_distrito INT UNSIGNED NOT NULL AUTO_INCREMENT,
  id_canton INT UNSIGNED NOT NULL,
  descripcion VARCHAR(100) NOT NULL,
  PRIMARY KEY (id_distrito),
  UNIQUE KEY uq_distrito_canton_descripcion (id_canton, descripcion),
  CONSTRAINT fk_distrito_canton FOREIGN KEY (id_canton) REFERENCES cantones(id_canton) ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT ck_distrito_descripcion CHECK (CHAR_LENGTH(TRIM(descripcion)) > 0 AND OCTET_LENGTH(descripcion) = OCTET_LENGTH(TRIM(descripcion)))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Referencia unica: el resto de la direccion se obtiene siguiendo las FK.
-- NULL mantiene compatibles los usuarios existentes y el registro actual.
ALTER TABLE persona
  ADD COLUMN id_distrito INT UNSIGNED NULL,
  ADD CONSTRAINT fk_persona_distrito FOREIGN KEY (id_distrito)
    REFERENCES distritos(id_distrito) ON DELETE RESTRICT ON UPDATE RESTRICT;

-- Objeto adicional de consulta, sin duplicar almacenamiento.
-- Claves globales: PA=pais, PR=provincia, CA=canton, DI=distrito.
CREATE VIEW tabla_general AS
  SELECT CONCAT('PA:', id_pais) AS id, descripcion,
         CAST(NULL AS CHAR(13)) AS id_padre FROM pais
  UNION ALL
  SELECT CONCAT('PR:', id_provincia), descripcion, CONCAT('PA:', id_pais) FROM provincias
  UNION ALL
  SELECT CONCAT('CA:', id_canton), descripcion, CONCAT('PR:', id_provincia) FROM cantones
  UNION ALL
  SELECT CONCAT('DI:', id_distrito), descripcion, CONCAT('CA:', id_canton) FROM distritos;
