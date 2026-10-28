#!/usr/bin/env python3
"""Generate a printable marker sheet for IRIS QR alignment.

One marker per A4 page, drawn as vector so module edges stay crisp at any
print size. Includes a 100 mm ruler so a mis-scaled printout is obvious.
"""
import argparse


class HelpFormatter(
    argparse.ArgumentDefaultsHelpFormatter,
    argparse.RawDescriptionHelpFormatter,
):
    pass


def positive_int(value):
    parsed = int(value)
    if parsed < 1:
        raise argparse.ArgumentTypeError("must be at least 1")
    return parsed


def positive_float(value):
    parsed = float(value)
    if parsed <= 0:
        raise argparse.ArgumentTypeError("must be greater than 0")
    return parsed


def build_parser():
    parser = argparse.ArgumentParser(
        description="Generate printable A4 QR markers for IRIS alignment.",
        formatter_class=HelpFormatter,
        epilog=(
            "examples:\n"
            "  %(prog)s\n"
            "  %(prog)s VENTION 4 markers.pdf\n"
            "  %(prog)s CELL_A 4 cell-a.pdf 240"
        ),
    )
    parser.add_argument(
        "environment",
        nargs="?",
        default="VENTION",
        help="environment identifier embedded in each marker",
    )
    parser.add_argument(
        "count",
        nargs="?",
        type=positive_int,
        default=4,
        help="number of markers to generate",
    )
    parser.add_argument(
        "output",
        nargs="?",
        default="markers.pdf",
        help="output PDF path",
    )
    parser.add_argument(
        "size_mm",
        nargs="?",
        type=positive_float,
        default=160.0,
        help="assembled QR size in millimetres; sizes above 185 use four sheets",
    )
    return parser


def load_rendering_dependencies(parser):
    global qrcode, A4, mm, canvas
    try:
        import qrcode
        from reportlab.lib.pagesizes import A4
        from reportlab.lib.units import mm
        from reportlab.pdfgen import canvas
    except ModuleNotFoundError as error:
        parser.error(
            f"missing dependency '{error.name}'; install with: "
            "python3 -m pip install qrcode reportlab"
        )


def draw_qr(c, payload, x_mm, y_mm, size_mm):
    """Draw the QR as filled vector squares. Returns module size in mm."""
    q = qrcode.QRCode(error_correction=qrcode.constants.ERROR_CORRECT_H, border=4)
    q.add_data(payload)
    q.make(fit=True)
    matrix = q.get_matrix()
    n = len(matrix)
    module = size_mm / n

    c.setFillColorRGB(0, 0, 0)
    for row_i, row in enumerate(matrix):
        run_start = None
        for col_i in range(n + 1):
            dark = col_i < n and row[col_i]
            if dark and run_start is None:
                run_start = col_i
            elif not dark and run_start is not None:
                # One rect per horizontal run: fewer ops, no seams between
                # adjacent modules that a rasteriser might leave as hairlines.
                c.rect((x_mm + run_start * module) * mm,
                       (y_mm + (n - row_i - 1) * module) * mm,
                       (col_i - run_start) * module * mm,
                       module * mm,
                       stroke=0, fill=1)
                run_start = None
    return module


def cover(c):
    c.setFont("Helvetica-Bold", 26)
    c.drawString(20 * mm, PAGE_H - 30 * mm, f"IRIS alignment markers - {ENVIRONMENT}")

    c.setFont("Helvetica", 11.5)
    lines = [
        "",
        "Placement",
        f"  - {ENVIRONMENT}_1 and {ENVIRONMENT}_2 go on the table edge where the robot sits.",
        "    The scene origin lands halfway between them, so the robot ends up at the",
        "    middle of that edge. Put them at opposite ends of the edge, as far apart as fits.",
        f"  - {ENVIRONMENT}_3 and {ENVIRONMENT}_4 go anywhere else on the same surface.",
        "",
        "Requirements",
        "  - All markers flat on the same surface. One sitting on a book tilts the whole scene.",
        "  - Not in a straight line. A well spread arrangement conditions the plane normal.",
        "  - Spread as wide as the surface allows: angular accuracy scales with separation.",
        "  - Mount rigid. A curled printout is a direct tilt error.",
        "",
        "No measuring needed",
        "  - Distances are never entered anywhere. The app learns the geometry from the",
        "    first successful alignment and checks later ones against it.",
        "",
        "Printing",
        "  - Print at 100% scale. Do not use 'fit to page' or 'shrink oversized pages'.",
        "  - Check the ruler on each page measures exactly 100 mm before mounting.",
        "",
        "Multiple environments",
        "  - Re-run this generator with a different name for each cell, e.g. CELL_A.",
        "    The app picks the environment from the payloads and recalls its saved offset.",
    ]
    y = PAGE_H - 45 * mm
    for line in lines:
        if line and not line.startswith("  "):
            c.setFont("Helvetica-Bold", 12.5)
        else:
            c.setFont("Helvetica", 11.5)
        c.drawString(20 * mm, y, line)
        y -= 6.6 * mm
    c.showPage()


def marker_page(c, payload):
    # Human label
    c.setFont("Helvetica-Bold", 40)
    c.drawCentredString(PAGE_W / 2, PAGE_H - 28 * mm, payload)

    c.setFont("Helvetica", 11)
    c.setFillColorRGB(0.35, 0.35, 0.35)
    c.drawCentredString(PAGE_W / 2, PAGE_H - 36 * mm,
                        f"IRIS alignment marker - environment {ENVIRONMENT}")
    c.setFillColorRGB(0, 0, 0)

    x_mm = (PAGE_W / mm - QR_MM) / 2.0
    y_mm = (PAGE_H / mm - QR_MM) / 2.0 - 8
    module = draw_qr(c, payload, x_mm, y_mm, QR_MM)

    # Crop marks, so it can be cut and mounted square
    c.setLineWidth(0.4)
    c.setStrokeColorRGB(0.6, 0.6, 0.6)
    for cx, cy in [(x_mm, y_mm), (x_mm + QR_MM, y_mm),
                   (x_mm, y_mm + QR_MM), (x_mm + QR_MM, y_mm + QR_MM)]:
        c.line((cx - 6) * mm, cy * mm, (cx + 6) * mm, cy * mm)
        c.line(cx * mm, (cy - 6) * mm, cx * mm, (cy + 6) * mm)

    # 100 mm ruler: if this does not measure 100 mm, the print was scaled.
    ruler_y = 26 * mm
    ruler_x0 = (PAGE_W - 100 * mm) / 2
    c.setStrokeColorRGB(0, 0, 0)
    c.setLineWidth(0.9)
    c.line(ruler_x0, ruler_y, ruler_x0 + 100 * mm, ruler_y)
    for i in range(11):
        tick = ruler_x0 + i * 10 * mm
        h = 3.2 * mm if i % 5 == 0 else 1.9 * mm
        c.line(tick, ruler_y, tick, ruler_y + h)

    c.setFont("Helvetica", 9.5)
    c.drawCentredString(PAGE_W / 2, ruler_y - 5.5 * mm,
                        "This line must measure exactly 100 mm - print at 100% scale")
    c.setFont("Helvetica", 8.5)
    c.setFillColorRGB(0.4, 0.4, 0.4)
    c.drawCentredString(PAGE_W / 2, 14 * mm,
                        f"payload \"{payload}\"   size {QR_MM:.0f} x {QR_MM:.0f} mm   "
                        f"module {module:.2f} mm   ECC level H")
    c.setFillColorRGB(0, 0, 0)
    c.showPage()


def tiled_marker_pages(c, payload):
    """Split one oversized marker across a 2x2 grid of A4 sheets."""
    half = QR_MM / 2.0
    margin = (PAGE_W / mm - half) / 2.0
    for row in range(2):
        for col in range(2):
            c.setFont("Helvetica-Bold", 20)
            c.drawString(12 * mm, PAGE_H - 14 * mm,
                         f"{payload}   sheet {row * 2 + col + 1} of 4   "
                         f"({'top' if row == 0 else 'bottom'}-{'left' if col == 0 else 'right'})")

            # Clip to this sheet's quadrant, then draw the whole QR offset so
            # only that quadrant lands on the page.
            c.saveState()
            path = c.beginPath()
            path.rect(margin * mm, (margin - 12) * mm, half * mm, half * mm)
            c.clipPath(path, stroke=0)
            draw_qr(c, payload,
                    margin - col * half,
                    (margin - 12) - (1 - row) * half,
                    QR_MM)
            c.restoreState()

            # Registration marks on the edges that join another sheet.
            c.setStrokeColorRGB(0.55, 0.55, 0.55)
            c.setLineWidth(0.4)
            c.setDash(3, 3)
            x0, y0 = margin * mm, (margin - 12) * mm
            x1, y1 = x0 + half * mm, y0 + half * mm
            if col == 0:
                c.line(x1, y0, x1, y1)
            else:
                c.line(x0, y0, x0, y1)
            if row == 0:
                c.line(x0, y0, x1, y0)
            else:
                c.line(x0, y1, x1, y1)
            c.setDash()

            c.setFont("Helvetica", 9)
            c.setFillColorRGB(0.4, 0.4, 0.4)
            c.drawString(12 * mm, 12 * mm,
                         "Trim to the dashed edges and tape to the other three sheets. "
                         f"Assembled size {QR_MM:.0f} x {QR_MM:.0f} mm.")
            c.setFillColorRGB(0, 0, 0)
            c.showPage()


def main(argv=None):
    global ENVIRONMENT, COUNT, OUT, QR_MM, PAGE_W, PAGE_H, TILE

    parser = build_parser()
    args = parser.parse_args(argv)
    if not args.environment.strip():
        parser.error("environment must not be empty")

    load_rendering_dependencies(parser)
    ENVIRONMENT = args.environment
    COUNT = args.count
    OUT = args.output
    QR_MM = args.size_mm
    PAGE_W, PAGE_H = A4

    # An A4 sheet can print about 190 mm across. Anything larger is split over
    # a 2x2 grid of sheets to be taped together.
    TILE = QR_MM > 185.0

    pdf = canvas.Canvas(OUT, pagesize=A4)
    pdf.setTitle(f"IRIS alignment markers - {ENVIRONMENT}")
    cover(pdf)
    for marker_index in range(1, COUNT + 1):
        payload = f"{ENVIRONMENT}_{marker_index}"
        if TILE:
            tiled_marker_pages(pdf, payload)
        else:
            marker_page(pdf, payload)
    pdf.save()

    sheets = COUNT * (4 if TILE else 1) + 1
    print(f"wrote {OUT}: {COUNT} markers at {QR_MM:.0f} mm"
          f"{' (tiled over 4 sheets each)' if TILE else ''}, {sheets} pages")


if __name__ == "__main__":
    main()
