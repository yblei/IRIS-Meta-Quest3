# Tools

Helper scripts that are not part of the Unity build. Kept outside `Assets/` so
Unity does not import them.

## `make_markers.py` — printable alignment markers

Generates the QR sheet the app aligns to: one marker per A4 page, drawn as
vector so module edges stay crisp at any print size.

```bash
pip install qrcode reportlab
python3 Tools/make_markers.py VENTION 4 ~/Downloads/IRIS-markers-VENTION.pdf
#                            ^env    ^count ^output
```

Each environment gets its own sheet. The app picks the environment from the
marker payloads (`VENTION_1`, `VENTION_2`, ...) and recalls that environment's
saved offset, so generate one sheet per cell:

```bash
python3 Tools/make_markers.py CELL_A 4 ~/Downloads/IRIS-markers-CELL_A.pdf
```

### What the pages contain

- Cover sheet with placement rules.
- One 160 mm QR per page, ECC level H, 4-module quiet zone, human-readable
  label on top.
- Corner crop marks for square mounting.
- A 100 mm ruler. **If it does not measure 100 mm on the printout, the printer
  scaled the page** — reprint at 100%, not "fit to page".

### Placement

- `<ENV>_1` and `<ENV>_2` go on the table edge where the robot sits, at
  opposite ends. The scene origin lands halfway between them, putting the robot
  at the middle of that edge.
- `<ENV>_3` and `<ENV>_4` go anywhere else on the same surface.
- All markers flat on one surface, not in a straight line, spread as wide as
  the surface allows.

No distances are ever measured or entered. The app learns the geometry from the
first successful alignment and checks later ones against it.

### Verifying a generated PDF

```bash
pip install pymupdf opencv-python
python3 - <<'PY'
import pymupdf, cv2, numpy as np
doc = pymupdf.open("markers.pdf")
det = cv2.QRCodeDetector()
for i, page in enumerate(doc):
    pix = page.get_pixmap(dpi=200)
    img = np.frombuffer(pix.samples, dtype=np.uint8).reshape(pix.height, pix.width, pix.n)
    gray = cv2.cvtColor(img, cv2.COLOR_RGB2GRAY if pix.n == 3 else cv2.COLOR_RGBA2GRAY)
    data, _, _ = det.detectAndDecode(gray)
    print(f"page {i+1}: {data or '(no QR — cover page)'}")
PY
```
