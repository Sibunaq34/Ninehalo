CREATE PROCEDURE sp_editar_perfil(
 IN p_actor INT UNSIGNED, IN p_usuario INT UNSIGNED,
 IN p_nombre VARCHAR(100), IN p_apellidos VARCHAR(100), IN p_telefono VARCHAR(20),
 IN p_correo VARCHAR(150) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci,
 IN p_id_pais INT, IN p_id_provincia INT, IN p_id_canton INT, IN p_id_distrito INT)
BEGIN
 DECLARE v_admin BOOLEAN DEFAULT FALSE;
 DECLARE v_correo_actual VARCHAR(150) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
 DECLARE v_persona INT UNSIGNED;
 DECLARE v_distrito INT UNSIGNED;
 DECLARE v_ubicacion BOOLEAN DEFAULT FALSE;
 DECLARE EXIT HANDLER FOR SQLEXCEPTION BEGIN ROLLBACK; RESIGNAL; END;
 SELECT EXISTS(SELECT 1 FROM usuario_rol ur JOIN rol r ON r.id_rol=ur.id_rol AND r.activo=1
   JOIN rol_permiso rp ON rp.id_rol=r.id_rol JOIN permiso pm ON pm.id_permiso=rp.id_permiso
   WHERE ur.id_usuario=p_actor AND pm.codigo='usuario.editar') INTO v_admin;
 IF p_actor IS NULL OR p_usuario IS NULL OR (p_actor<>p_usuario AND NOT v_admin) THEN
   SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Permiso denegado';
 END IF;
 IF p_nombre IS NULL OR TRIM(p_nombre)='' OR p_apellidos IS NULL OR TRIM(p_apellidos)=''
   OR p_correo IS NULL OR TRIM(p_correo)='' THEN
   SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Campos obligatorios';
 END IF;
 SET v_ubicacion=(p_id_pais IS NOT NULL OR p_id_provincia IS NOT NULL OR p_id_canton IS NOT NULL OR p_id_distrito IS NOT NULL);
 IF v_ubicacion AND (p_id_pais IS NULL OR p_id_pais<=0 OR p_id_provincia IS NULL OR p_id_provincia<=0
   OR p_id_canton IS NULL OR p_id_canton<=0 OR p_id_distrito IS NULL OR p_id_distrito<=0) THEN
   SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Ubicación geográfica inválida';
 END IF;
 START TRANSACTION;
 SELECT correo INTO v_correo_actual FROM usuario WHERE id_usuario=p_usuario FOR UPDATE;
 SELECT id_persona INTO v_persona FROM persona WHERE id_usuario=p_usuario FOR UPDATE;
 IF v_correo_actual IS NULL OR v_persona IS NULL THEN
   SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Perfil no existe';
 END IF;
 IF v_ubicacion THEN
   SELECT d.id_distrito INTO v_distrito FROM distritos d
   JOIN cantones c ON c.id_canton=d.id_canton
   JOIN provincias pr ON pr.id_provincia=c.id_provincia
   JOIN pais pa ON pa.id_pais=pr.id_pais
   WHERE d.id_distrito=p_id_distrito AND c.id_canton=p_id_canton
     AND pr.id_provincia=p_id_provincia AND pa.id_pais=p_id_pais FOR SHARE;
   IF v_distrito IS NULL THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Ubicación geográfica inválida'; END IF;
 END IF;
 UPDATE usuario SET correo=LOWER(TRIM(p_correo)),
   correo_verificado=IF(v_correo_actual=LOWER(TRIM(p_correo)),correo_verificado,0)
 WHERE id_usuario=p_usuario;
 UPDATE persona SET nombre=TRIM(p_nombre),apellidos=TRIM(p_apellidos),telefono=p_telefono,
   id_distrito=IF(v_ubicacion,v_distrito,id_distrito) WHERE id_persona=v_persona;
 CALL sp_registrar_bitacora(p_actor,'PERFIL_EDITADO','usuario');
 COMMIT;
END;
