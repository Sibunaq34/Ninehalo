# Carrito y finalización

El carrito es local a la pestaña y a la cuenta autenticada. Guarda únicamente identificadores y cantidades; sus precios se vuelven a consultar al abrirlo. No puede añadirse ni consultarse sin sesión. Límites: 10 licencias diferentes, 20 unidades por licencia y 50 unidades totales. La navegación inferior tiene Inicio, Carrito, Estadísticas, Perfil y Ayuda.

POST `/api/compras/carrito` requiere Bearer JWT. Body: `clave_operacion` UUID y `items: [{ id_licencia, cantidad, precio_unitario }]`. El precio enviado se usa únicamente para detectar cambios respecto al formulario: el importe registrado procede de la base de datos. La cuenta debe estar activa y tener el correo verificado. No se reciben datos de tarjeta y no se procesan cargos.

Cada unidad se registra como una compra independiente PENDIENTE con el precio, duración y límite de juegos actuales. Todo el pedido y su bitácora se guardan en una transacción: cualquier error revierte todas las unidades. Las referencias `cart:<usuario>:<UUID>:<unidad>` y un bloqueo MySQL por operación permiten repetir la confirmación sin duplicar las compras, sin modificar el esquema de datos. Se deben reservar esas referencias para este endpoint.

Respuesta 201: `{ success:true, message, data:{ clave_operacion, estado:"PENDIENTE", total, compras:[{ id_compra,id_licencia,precio_pagado,estado }] } }`. La misma operación devuelve los mismos identificadores en reintentos. Un UUID no puede reutilizarse con productos, cantidades o precios diferentes. La clave permanece en la pestaña hasta confirmar el resultado; si la conexión falla, el carrito se bloquea para modificaciones y el usuario puede reintentar el mismo pedido.

Errores: 400 datos inválidos, 401 sesión inválida, 403 cuenta no habilitada, 409 precio/disponibilidad modificados o conflicto de operación, 500 fallo interno. `rechazado:true` indica que no se creó el pedido y permite editar el carrito; si es false o no se recibe una respuesta definitiva, se conserva la clave del pedido para consultar/reintentar.

El formulario presenta el resumen completo y solicita confirmar la revisión de licencias y cantidades. Al recibir la confirmación, retira sólo las unidades confirmadas del carrito. No afirma que el pago o la activación se hayan realizado; ambos requieren un flujo de pagos de confianza independiente.
