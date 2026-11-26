CREATE PROCEDURE sp_consultar_perfil(IN p_actor INT UNSIGNED, IN p_usuario INT UNSIGNED)
READS SQL DATA
BEGIN
 IF p_actor IS NULL OR p_usuario IS NULL OR (p_actor<>p_usuario AND NOT EXISTS(
   SELECT 1 FROM usuario_rol ur JOIN rol r ON r.id_rol=ur.id_rol AND r.activo=1
   JOIN rol_permiso rp ON rp.id_rol=r.id_rol JOIN permiso pm ON pm.id_permiso=rp.id_permiso
   WHERE ur.id_usuario=p_actor AND pm.codigo='usuario.leer')) THEN
   SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Permiso denegado';
 END IF;
 SELECT u.id_usuario,u.correo,u.correo_verificado,u.estado,
        p.identificacion,p.nombre,p.apellidos,p.telefono,
        pa.id_pais,pr.id_provincia,c.id_canton,d.id_distrito,
        pa.descripcion AS pais,pr.descripcion AS provincia,
        c.descripcion AS canton,d.descripcion AS distrito
 FROM usuario u JOIN persona p ON p.id_usuario=u.id_usuario
 LEFT JOIN distritos d ON d.id_distrito=p.id_distrito
 LEFT JOIN cantones c ON c.id_canton=d.id_canton
 LEFT JOIN provincias pr ON pr.id_provincia=c.id_provincia
 LEFT JOIN pais pa ON pa.id_pais=pr.id_pais
 WHERE u.id_usuario=p_usuario;
END;
