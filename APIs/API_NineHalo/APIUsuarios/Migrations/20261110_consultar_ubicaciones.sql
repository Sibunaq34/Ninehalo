-- MySQL 8. Ejecutar despues de crear tabla_general.
-- Un solo CREATE PROCEDURE, ejecutable desde el cliente .NET sin DELIMITER.
-- En Workbench envolver con DELIMITER $$ y finalizar END$$.
CREATE PROCEDURE sp_consultar_ubicaciones(IN p_id_padre VARCHAR(13) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci)
READS SQL DATA
BEGIN
  IF p_id_padre IS NOT NULL THEN
    -- Solo pais, provincia o canton pueden ser padres de otra seleccion.
    IF NOT REGEXP_LIKE(p_id_padre, '^(PA|PR|CA):[1-9][0-9]{0,9}$', 'c') THEN
      SIGNAL SQLSTATE '45000' SET MYSQL_ERRNO=30001,
        MESSAGE_TEXT='Identificador de ubicación inválido.';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM tabla_general WHERE id COLLATE utf8mb4_unicode_ci=p_id_padre) THEN
      SIGNAL SQLSTATE '45000' SET MYSQL_ERRNO=30002,
        MESSAGE_TEXT='La ubicación seleccionada no existe.';
    END IF;
  END IF;

  -- Identificador numerico para conservar el contrato del frontend.
  -- id_general e id_padre conservan las claves jerarquicas de la vista.
  SELECT CAST(SUBSTRING(id, 4) AS UNSIGNED) AS id,
         descripcion, id AS id_general, id_padre
  FROM tabla_general
  WHERE (p_id_padre IS NULL AND id_padre IS NULL)
     OR (p_id_padre IS NOT NULL AND id_padre COLLATE utf8mb4_unicode_ci=p_id_padre)
  ORDER BY descripcion, id;
END;
