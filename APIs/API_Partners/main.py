import hashlib
import os
from contextlib import contextmanager
from datetime import date
from pathlib import Path
from typing import Annotated

import mysql.connector
from dotenv import load_dotenv
from fastapi import Depends, FastAPI, HTTPException, Path as ApiPath, Response, Security
from fastapi.responses import JSONResponse
from fastapi.security import APIKeyHeader
from pydantic import BaseModel, ConfigDict, Field

load_dotenv(Path(__file__).with_name(".env"))
app = FastAPI(title="NineHalo Partner API", version="1.0")
router_key = APIKeyHeader(name="X-Partner-Key", auto_error=False)
prefix = "/api/partners/v1"


@contextmanager
def database():
    connection = mysql.connector.connect(
        host=os.environ.get("DB_HOST", "127.0.0.1"),
        port=int(os.environ.get("DB_PORT", "3306")),
        user=os.environ.get("DB_USER", "ninehalo_partners"),
        password=os.environ.get("DB_PASSWORD", ""),
        database="nine_halo",
        connection_timeout=5,
    )
    try:
        with connection.cursor(dictionary=True) as cursor:
            yield cursor
        connection.commit()
    finally:
        connection.close()


@app.exception_handler(mysql.connector.Error)
async def database_error(request, error):
    return JSONResponse(status_code=503, content={"detail": "Servicio temporalmente no disponible."})


def authenticate(key: Annotated[str | None, Security(router_key)]) -> int:
    if not key or not 32 <= len(key) <= 128:
        raise HTTPException(401, "Credencial de socio inválida.")
    with database() as cursor:
        cursor.execute(
            "SELECT id FROM partner_comercial WHERE clave_hash=%s AND activo=1",
            (hashlib.sha256(key.encode()).digest(),),
        )
        partner = cursor.fetchone()
    if partner is None:
        raise HTTPException(401, "Credencial de socio inválida.")
    return partner["id"]


Partner = Annotated[int, Depends(authenticate)]
order_fields = """CAST(id AS CHAR) AS id, referencia, id_licencia, cantidad,
    precio_unitario, precio_unitario*cantidad AS total_usd, estado, creado_utc,
    confirmado_utc, vence_utc"""


class OrderRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")
    referencia: str = Field(pattern=r"^[A-Za-z0-9._:-]{1,80}$")
    id_licencia: int = Field(strict=True, gt=0, le=2147483647)
    cantidad: int = Field(default=1, strict=True, ge=1, le=50)


@app.get(prefix + "/licencias")
def catalog(partner: Partner):
    with database() as cursor:
        cursor.execute("""SELECT id_licencia, nombre, precio AS precio_usd,
            duracion_meses, max_juegos FROM licencia WHERE activo=1
            ORDER BY precio, id_licencia""")
        licenses = cursor.fetchall()
        by_id = {row["id_licencia"]: row for row in licenses}
        for row in licenses:
            row["detalles"] = []
        cursor.execute("""SELECT d.id_licencia, d.descripcion FROM detalle_licencia d
            JOIN licencia l ON l.id_licencia=d.id_licencia AND l.activo=1
            ORDER BY d.id_licencia, d.orden, d.id_detalle""")
        for detail in cursor.fetchall():
            if detail["id_licencia"] in by_id:
                by_id[detail["id_licencia"]]["detalles"].append(detail["descripcion"])
        return {"data": licenses}


@app.post(prefix + "/pedidos", status_code=201, responses={200: {"description": "Reintento del mismo pedido"}})
def create_order(request: OrderRequest, partner: Partner, response: Response):
    with database() as cursor:
        # Serialize concurrent orders for this partner, including retries of a reference.
        cursor.execute("SELECT GET_LOCK(%s, 5) AS acquired", (f"ninehalo:partner:{partner}",))
        if cursor.fetchone()["acquired"] != 1:
            raise HTTPException(503, "El pedido se está procesando. Reintenta con la misma referencia.")
        cursor.execute("SELECT id FROM partner_comercial WHERE id=%s AND activo=1", (partner,))
        if cursor.fetchone() is None:
            raise HTTPException(401, "Credencial de socio inválida.")
        cursor.execute(
            f"SELECT {order_fields} FROM partner_pedido WHERE socio_id=%s AND referencia=%s",
            (partner, request.referencia),
        )
        order = cursor.fetchone()
        if order is not None:
            if (order["id_licencia"], order["cantidad"]) != (request.id_licencia, request.cantidad):
                raise HTTPException(409, "La referencia ya corresponde a otro pedido.")
            response.status_code = 200
            return {"data": order}
        cursor.execute(
            "SELECT precio FROM licencia WHERE id_licencia=%s AND activo=1",
            (request.id_licencia,),
        )
        license_row = cursor.fetchone()
        if license_row is None:
            raise HTTPException(422, "Licencia no disponible.")
        cursor.execute("""INSERT INTO partner_pedido
            (socio_id, referencia, id_licencia, cantidad, precio_unitario, creado_utc)
            VALUES (%s,%s,%s,%s,%s,UTC_TIMESTAMP())""",
            (partner, request.referencia, request.id_licencia, request.cantidad, license_row["precio"]),
        )
        cursor.execute(f"SELECT {order_fields} FROM partner_pedido WHERE id=%s", (cursor.lastrowid,))
        return {"data": cursor.fetchone()}


@app.get(prefix + "/pedidos/{id}")
def get_order(id: Annotated[str, ApiPath(pattern=r"^[1-9][0-9]{0,19}$")], partner: Partner):
    with database() as cursor:
        cursor.execute(f"SELECT {order_fields} FROM partner_pedido WHERE id=%s AND socio_id=%s",
                       (int(id), partner))
        order = cursor.fetchone()
    if order is None:
        raise HTTPException(404, "Pedido no encontrado.")
    return {"data": order}


@app.get(prefix + "/estadisticas")
def statistics(desde: date, hasta: date, partner: Partner):
    if hasta < desde or (hasta - desde).days > 366:
        raise HTTPException(422, "Indica un rango válido de hasta 367 días calendario.")
    with database() as cursor:
        cursor.execute("""SELECT id_licencia, COUNT(*) AS pedidos, SUM(cantidad) AS unidades,
            SUM(cantidad*precio_unitario) AS total_usd
            FROM partner_pedido WHERE socio_id=%s AND estado='CONFIRMADO'
            AND confirmado_utc >= %s AND confirmado_utc < DATE_ADD(%s, INTERVAL 1 DAY)
            GROUP BY id_licencia ORDER BY id_licencia""", (partner, desde, hasta))
        return {"data": cursor.fetchall()}
