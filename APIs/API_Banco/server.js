import 'dotenv/config';
import app from './src/app.js';

const port = Number(process.env.PORT || 3005);
app.listen(port, () => console.log(`API Banco disponible en http://localhost:${port}`));
