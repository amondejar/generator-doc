# Instrucciones: supuestos prácticos de Lenguajes de Marcas (LMSGI · 1.º DAM)

## Parámetros de este trabajo (edita solo esta sección)

- **Unidad o tema:** UT01 · XML (documento bien formado, atributos frente a elementos, entidades, espacios de nombres)
- **Número de supuestos:** 2
- **Duración de cada supuesto:** 60 minutos
- **Dificultad:** media, como el examen
- **Preguntas tipo test:** 10

## Objetivo

Preparo el examen de LMSGI. Tiene dos partes: un test y un supuesto práctico que se hace en ordenador con VS Code y el navegador.
A partir de la documentación de /mnt/session/uploads/ (apuntes de las unidades, apuntes del profesor, ejemplos guiados y enunciados de tareas anteriores), genera material de práctica **nuevo** con el mismo estilo que esos enunciados.

Reglas sobre el contenido:
- Usa solo lo que aparece en la documentación. No metas tecnologías o etiquetas que no estén en los apuntes.
- Cuando los apuntes y el estándar no coincidan, sigue el criterio de los apuntes e indícalo en la corrección.
- Los contextos deben ser **inventados y distintos** de las tareas ya hechas (reciclaje de móviles, TechDAM, taller AutoCarthago). Usa empresas creíbles de la zona de Cartagena y Murcia.
- Los datos de ejemplo tienen que ser coherentes: DNI de 8 cifras y letra, fechas `AAAA-MM-DD` y precios realistas.

## Ficheros que tienes que crear en /mnt/session/outputs/

Para cada supuesto, siendo NN su número (01, 02…) y `tema` una palabra sin tildes ni espacios:

### 1. `supuesto_NN_tema.md`: el enunciado (sin soluciones)

Usa exactamente esta estructura:

```
# Supuesto práctico NN — <Nombre de la empresa> (<duración> minutos)

<Párrafo de contexto: quién es la empresa, qué necesita y para qué. 3–5 líneas.>

Diseña el fichero `<nombre>.xml` (o `index.html` + `styles.css`) con esta información:

**1. <Bloque de datos>.** <Qué datos tiene cada elemento, con listas de valores cerrados entre paréntesis.>
**2. <Bloque de datos>.** ...
**3. ...**
**4. ...** (incluye siempre algo que obligue a usar lo más preguntado: espacio de nombres, entidades, relación entre bloques…)

**Requisitos:**
- <Requisito técnico concreto y comprobable>
- Al menos 3 ejemplos de cada elemento, con algún caso especial (opcional vacío, varios hijos…).
- Sin repetir datos: unos bloques citan a otros por código.
- <Textos concretos que obliguen a usar entidades, por ejemplo "Pérez & Hijos" o "temperatura < 5 °C">
- Comentarios que separen cada bloque.
- Comentario final de justificación del diseño (5–8 líneas): qué va como atributo, qué como elemento y por qué.

**Cómo comprobarlo:** <por ejemplo: ábrelo con doble clic en el navegador; si está bien formado verás el árbol.>

**Entrega:** <nombre de fichero(s) sin tildes ni espacios>.
```

### 2. `correccion_NN_tema.md`: rúbrica para corregirlo

- Tabla de criterios con su puntuación, sobre 10 en total, agrupados en: **bien formado**, **diseño**, **datos y coherencia**, **requisitos del enunciado**.
- Lista de los **errores típicos** que se esperan en este supuesto y cuánto restan cada uno.
- Para cada criterio, cómo se comprueba en menos de un minuto.

### 3. `solucion_NN_tema/`: solución de referencia

- Los ficheros completos (`.xml`, o bien `index.html` y `styles.css`), listos para abrir.
- Deben cumplir todos los requisitos del enunciado y estar bien formados.
- Comentarios breves solo donde haya una decisión que justificar.

### 4. `test_NN.md` y `test_NN_soluciones.md`: preguntas tipo test

- `test_NN.md`: preguntas numeradas, cada una con 4 opciones (a, b, c, d) y una sola correcta. Sin soluciones.
- `test_NN_soluciones.md`: la letra correcta y una explicación de 1 o 2 líneas que cite el apartado de los apuntes de donde sale.
- Mezcla teoría (definiciones, reglas) con preguntas sobre fragmentos de código ("¿está bien formado?", "¿cuál es válido?").

### 5. `indice.md`

Lista de todos los ficheros generados con una línea que diga qué es cada uno, y el orden recomendado:
1. test;
2. supuesto, cronometrado y sin mirar apuntes;
3. corregir con la rúbrica;
4. solución.

## Comprobación final antes de terminar

- Cada XML de las soluciones está bien formado: un solo elemento raíz, todas las etiquetas cerradas, atributos entre comillas y entidades en `&` y `<`.
- Cada HTML de las soluciones no tiene `style="..."` ni etiquetas obsoletas, y cada `label for` coincide con un `id`.
- Las puntuaciones de cada rúbrica suman 10.
- Ningún enunciado contiene la solución.
