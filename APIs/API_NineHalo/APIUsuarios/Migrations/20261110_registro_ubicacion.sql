-- Ejecutar despues de 20261110_ubicacion_geografica.sql.
-- Un solo CREATE PROCEDURE; desde Workbench usar DELIMITER $$ antes
-- y terminar el CREATE con $$ en lugar del ultimo punto y coma.
CREATE PROCEDURE sp_registrar_usuario_ubicacion(
 IN p_identificacion VARCHAR(20), IN p_nombre VARCHAR(100),
 IN p_apellidos VARCHAR(100), IN p_telefono VARCHAR(20),
 IN p_correo VARCHAR(150), IN p_password_hash VARCHAR(255),
 IN p_id_pais INT, IN p_id_provincia INT,
 IN p_id_canton INT, IN p_id_distrito INT)
BEGIN
 DECLARE v_usuario INT UNSIGNED;
 DECLARE v_rol INT UNSIGNED;
 DECLARE v_distrito INT UNSIGNED;
 DECLARE EXIT HANDLER FOR SQLEXCEPTION BEGIN ROLLBACK; RESIGNAL; END;
 IF p_correo IS NULL OR TRIM(p_correo)='' OR p_password_hash IS NULL OR TRIM(p_password_hash)='' OR p_identificacion IS NULL OR TRIM(p_identificacion)='' OR p_nombre IS NULL OR TRIM(p_nombre)='' OR p_apellidos IS NULL OR TRIM(p_apellidos)='' THEN
   SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Campos obligatorios incompletos';
 END IF;
 START TRANSACTION;
 -- Bloquear la cadena hasta guardar el usuario evita cambios de padre concurrentes.
 SELECT d.id_distrito INTO v_distrito
 FROM distritos d JOIN cantones c ON c.id_canton=d.id_canton
 JOIN provincias pr ON pr.id_provincia=c.id_provincia
 JOIN pais pa ON pa.id_pais=pr.id_pais
 WHERE d.id_distrito=p_id_distrito AND c.id_canton=p_id_canton
   AND pr.id_provincia=p_id_provincia AND pa.id_pais=p_id_pais
 FOR SHARE;
 IF v_distrito IS NULL THEN
   SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Ubicación geográfica inválida';
 END IF;
 SELECT id_rol INTO v_rol FROM rol WHERE codigo='USUARIO' AND activo=1 LIMIT 1;
 IF v_rol IS NULL THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Rol USUARIO no configurado'; END IF;
 INSERT INTO usuario(correo,password_hash) VALUES(LOWER(TRIM(p_correo)),p_password_hash);
 SET v_usuario=LAST_INSERT_ID();
 INSERT INTO persona(id_usuario,identificacion,nombre,apellidos,telefono,id_distrito)
 VALUES(v_usuario,TRIM(p_identificacion),TRIM(p_nombre),TRIM(p_apellidos),p_telefono,v_distrito);
 INSERT INTO usuario_rol(id_usuario,id_rol) VALUES(v_usuario,v_rol);
 CALL sp_registrar_bitacora(v_usuario,'USUARIO_REGISTRADO','usuario');
 COMMIT;
 SELECT v_usuario AS id_usuario;
END;
