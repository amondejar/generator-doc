# Documentos en PDF a partir de las indicaciones

Genera los documentos que se piden en «Indicaciones de este trabajo» usando la documentación de /mnt/session/uploads/ y, si no hay, lo que tengas guardado en la memoria.

- Si en uploads hay **fotos o escaneos** (por ejemplo, un enunciado en papel), léelos con atención y transcribe lo necesario antes de empezar. Si algo no se lee bien, indícalo en el resumen en vez de suponerlo.
- Entrega cada documento en **PDF** en /mnt/session/outputs/, con nombres descriptivos sin tildes ni espacios.
- Escribe primero cada documento en Markdown en /tmp/md/ y conviértelo con la librería `markdown-pdf` (`from markdown_pdf import MarkdownPdf, Section`). Si no está, prueba `pip install markdown-pdf` y, si no puedes, usa `reportlab`.
- Comprueba que cada PDF se abre, tiene texto y muestra bien las tildes y la ñ. No dejes ficheros intermedios en outputs.
- Si generas código (XML, HTML, CSS…), entrégalo también como fichero en su formato original, además de incluirlo en el PDF.
