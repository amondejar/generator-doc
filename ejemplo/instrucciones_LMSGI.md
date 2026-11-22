# Instrucciones: supuestos prácticos de Lenguajes de Marcas (LMSGI · 1.º DAM)

## Parámetros de este trabajo (edita solo esta sección)

- **Unidad o tema:** UT01 · XML (documento bien formado, diseño operativo, atributos frente a elementos, entidades, espacios de nombres)
- **Número de supuestos:** 2
- **Duración de cada supuesto:** 60 minutos
- **Dificultad:** media, como el examen
- **Preguntas tipo test:** 10

## Objetivo

Preparo el examen de LMSGI. Tiene dos partes: un test y un supuesto práctico que se hace en ordenador con VS Code y el navegador.

En /mnt/session/uploads/ tienes dos tipos de documentos:
- **Enunciados de tareas del módulo**: ficheros cuyo texto empieza por «LMSGI > UTxx > Tarea». Úsalos solo como **modelo de formato y de nivel**. No los resuelvas ni generes material sobre esos mismos casos.
- **Apuntes**: unidades, apuntes del profesor y ejemplos guiados. De aquí sale el contenido técnico.

Genera material de práctica **nuevo**, con el mismo estilo que los enunciados de las tareas.

Reglas sobre el contenido:
- Usa solo lo que aparece en los apuntes. No metas tecnologías o etiquetas que no estén en ellos.
- Cuando los apuntes y el estándar no coincidan, sigue el criterio de los apuntes e indícalo en la corrección.
- Los contextos deben ser **inventados y distintos** de los casos de los enunciados subidos y de los ya practicados (reciclaje de móviles, TechDAM, taller AutoCarthago). Usa empresas creíbles de la zona de Cartagena y Murcia.
- Los datos de ejemplo tienen que ser coherentes: DNI de 8 cifras y letra, fechas `AAAA-MM-DD` y precios realistas.

## Formato del enunciado (igual que las tareas del módulo)

Cada supuesto sigue este orden:

1. **Cabecera:** «LMSGI > Supuesto práctico NN · <tema> (<duración> minutos)».
2. **Enunciado:** párrafo de contexto (quién es la empresa y qué necesita) y las partes de la información, con viñetas y subniveles cuando haya variantes (como C1/C2/V en la tarea de la UT01).
3. **Requisitos concretos y comprobables:**
   - mínimo de 3 ejemplos de cada elemento;
   - evitar repetir datos;
   - textos que obliguen a usar entidades;
   - comentarios;
   - lo que pida la unidad.
4. **Criterios de puntuación, sobre 10 puntos:** el mismo reparto que usan los enunciados subidos. Para XML: 2,5 bien formado · 2,5 diseño operativo · 2,5 uso correcto de atributos · 2,5 datos de ejemplo suficientes, variados y coherentes. Añade que influye la justificación del diseño.
5. **Indicaciones de entrega de la práctica:** qué ficheros crear y con qué nombre. Usa nombres genéricos de práctica, como `supuesto01.xml` o `justificacion.txt`. **No** uses el formato `apellido1_apellido2_nombre_LMSGIxx_Tarea`, que es el de las entregas reales.

## Ficheros que tienes que crear en /mnt/session/outputs/

Todos los documentos para leer van en **PDF**. Los ficheros de código de las soluciones van además en su formato original, para poder abrirlos en VS Code y en el navegador.

Para cada supuesto (NN = 01, 02…; `tema` = una palabra sin tildes ni espacios):

| Fichero | Contenido |
|---|---|
| `NN_supuesto_tema.pdf` | Enunciado con el formato anterior, sin soluciones. |
| `NN_correccion_tema.pdf` | Rúbrica con los 4 criterios desglosados en puntos comprobables (suman 10), errores típicos con lo que restan, y cómo comprobar cada criterio en menos de un minuto. |
| `NN_solucion_tema.pdf` | Solución de referencia completa, con el código formateado y una justificación del diseño de 5–10 líneas. |
| `NN_solucion_tema/` | Los mismos ficheros de la solución listos para abrir (`.xml`, o `index.html` + `styles.css`) y `justificacion.txt`. |
| `NN_test.pdf` | Preguntas tipo test numeradas, 4 opciones (a–d) y una sola correcta. Sin soluciones. Mezcla teoría y fragmentos de código. |
| `NN_test_soluciones.pdf` | Letra correcta de cada pregunta y explicación de 1 o 2 líneas que cite el apartado de los apuntes. |

Y uno general:

| Fichero | Contenido |
|---|---|
| `00_indice.pdf` | Lista de ficheros con una línea cada uno y el orden de trabajo: test → supuesto cronometrado sin apuntes → corrección con la rúbrica → solución. |

## Cómo generar los PDF

1. Escribe cada documento primero en Markdown en una carpeta de trabajo (por ejemplo `/tmp/md/`), **no** en outputs.
2. Conviértelo a PDF con Python y la librería `markdown-pdf`:
   ```python
   from markdown_pdf import MarkdownPdf, Section
   pdf = MarkdownPdf(toc_level=2)
   pdf.add_section(Section(open("/tmp/md/01_supuesto_tema.md", encoding="utf-8").read()))
   pdf.meta["title"] = "Supuesto práctico 01"
   pdf.save("/mnt/session/outputs/01_supuesto_tema.pdf")
   ```
3. Si `markdown-pdf` no está instalada, prueba `pip install markdown-pdf`. Si tampoco puedes, genera el PDF con `reportlab`. No entregues los documentos en `.md` ni en `.txt` como sustituto del PDF, salvo `justificacion.txt` dentro de la carpeta de la solución.
4. Comprueba que cada PDF se abre, tiene texto (no está vacío) y que las tildes y la ñ se ven bien. Para eso, extrae el texto con PyMuPDF (`import fitz`).

## Comprobación final antes de terminar

- Cada XML de las soluciones está bien formado: un solo elemento raíz, todas las etiquetas cerradas, atributos entre comillas y entidades en `&` y `<`. Compruébalo con Python (`xml.etree.ElementTree.parse`).
- Cada HTML de las soluciones no tiene `style="..."` ni etiquetas obsoletas, y cada `label for` coincide con un `id`.
- Las puntuaciones de cada rúbrica suman 10.
- Ningún enunciado contiene la solución, y ninguno reproduce los casos de los enunciados subidos.
- En /mnt/session/outputs/ solo quedan los PDF y las carpetas de solución: ni los `.md` intermedios ni ficheros temporales.
