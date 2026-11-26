import 'dotenv/config';
import app from './src/app.js';

const port = Number(process.env.PORT || 3006);
if (!Number.isInteger(port) || port < 1 || port > 65535 || port === 3005) {
  throw new Error('PORT debe ser un puerto válido distinto de 3005; usa 3006 para esta API.');
}
app.listen(port, () => console.log(`API TSE disponible en http://localhost:${port}`));
