import mysql from 'mysql2/promise';

const pool = mysql.createPool({
  host: process.env.DB_HOST || '127.0.0.1',
  port: Number(process.env.DB_PORT || 3306),
  user: process.env.DB_USER,
  password: process.env.DB_PASSWORD,
  database: process.env.DB_NAME || 'promohub_db',
  connectionLimit: 5,
  supportBigNumbers: true,
  bigNumberStrings: true,
  dateStrings: true
});

export async function call(statement, params = []) {
  const [resultSets] = await pool.execute(statement, params);
  return resultSets[0];
}




export async function query(statement, params = []) { const [rows] = await pool.execute(statement, params); return rows; }
