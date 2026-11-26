# API TSE (simulador)

Proyecto educativo con personas ficticias. No consulta al TSE real. Requiere Node.js 20.6 o posterior, pnpm y MySQL 8.

## Preparación e inicio

1. Abre una terminal en `D:\Universidad\Programacion\JavaScript\NineHalo\APIs\API_TSE` y ejecuta `pnpm install`.
2. Configura `.env` con `DB_HOST`, `DB_USER`, `DB_PASSWORD`, `DB_NAME=tse_db` y `PORT=3006`. Usa `.env.example` como referencia; `.env` no se versiona.
3. Abre `database/schema.sql` en MySQL Workbench y ejecútalo. También puedes abrir `mysql -u root -p` y ejecutar:

```sql
SOURCE D:/Universidad/Programacion/JavaScript/NineHalo/APIs/API_TSE/database/schema.sql;
```

El script crea `tse_db`, inserta ocho personas sin sobrescribir las existentes y crea el procedimiento de consulta. Puede repetirse.

4. Ejecuta `pnpm dev` (recarga automática) o `pnpm start`. La API usa **3006**; el banco puede continuar en **3005**.
5. Ejecuta `pnpm test` para las pruebas del validador.

## Probar en Postman

Selecciona GET. No se envía body ni se requiere autenticación.

| URL | Resultado |
| --- | --- |
| `http://localhost:3006/api/personas/102340567` | 200, José Hernández Rojas |
| `http://localhost:3006/api/personas/1-0234-0567` | 200, misma persona, cédula normalizada |
| `http://localhost:3006/api/personas/1%200234%200567` | 200, acepta espacios codificados |
| `http://localhost:3006/api/personas/123` | 400, longitud inválida |
| `http://localhost:3006/api/personas/10234056a` | 400, carácter inválido |
| `http://localhost:3006/api/personas/` | 400, cédula obligatoria |
| `http://localhost:3006/api/personas/999999999` | 404, no existe |

Respuesta de éxito:

```json
{
  "ok": true,
  "mensaje": "Persona encontrada",
  "data": {
    "cedula": "102340567",
    "nombre": "José",
    "primerApellido": "Hernández",
    "segundoApellido": "Rojas"
  }
}
```

Respuesta de error:

```json
{ "ok": false, "mensaje": "No existe una persona registrada con esa cédula." }
```

Los fallos internos o de conexión devuelven 500 con un mensaje genérico. CORS está habilitado. La API valida exactamente nueve dígitos, permitiendo guiones y espacios, antes de consultar MySQL mediante `CALL sp_obtener_persona_por_cedula(?)`. El procedimiento no valida datos. Las capas de rutas, controladores, servicios y validadores permiten agregar endpoints sin reorganizar el proyecto.

## Cédulas ficticias disponibles

`101110111` (Ana Rodríguez Vargas), `202220222` (Luis Mora Jiménez), `303330333` (María Solís Castro), `102340567` (José Hernández Rojas), `204560789` (Laura Chaves Ramírez), `305670890` (Carlos Vargas Mora), `406780901` (Sofía Jiménez Alvarado), `507890123` (Andrés Campos Sánchez).

Las tres primeras coinciden con los clientes de prueba originales de API_Banco. Ambas bases de datos son independientes.
