"""Erzeugt die statischen Schnitte von Roboto Flex und Roboto Mono für die WPF-Oberfläche.

WPF wertet die Gewichtsachse einer variablen Schrift nicht aus: Es zeigt den Standardschnitt und
fettet für SemiBold künstlich nach. Deshalb liegen unter src/MdExplorer.App/Assets/Fonts feste
Schnitte, aus den variablen Dateien des Google-Fonts-Repositorys erzeugt (ofl/robotoflex,
ofl/robotomono), auf Latein und Latein-Erweitert zugeschnitten.

Aufruf (braucht fontTools):
    python tools/schriften-statisch.py <Ordner mit RobotoFlex-VF.ttf und RobotoMono-VF.ttf>
"""

import os
import sys

from fontTools import subset
from fontTools.ttLib import TTFont
from fontTools.varLib import instancer

ZIEL = os.path.join(os.path.dirname(__file__), "..", "src", "MdExplorer.App", "Assets", "Fonts")

# Latein und Latein-Erweitert in den Bereichen, mit denen Google Fonts zuschneidet.
BEREICHE = [
    (0x0000, 0x00FF), (0x0100, 0x02BA), (0x02BB, 0x02BC), (0x02C6, 0x02C6), (0x02DA, 0x02DA),
    (0x02DC, 0x02DC), (0x0300, 0x0301), (0x0303, 0x0304), (0x0308, 0x0309), (0x0323, 0x0323),
    (0x0329, 0x0329), (0x1D00, 0x1DBF), (0x1E00, 0x1E9F), (0x1EF2, 0x1EFF), (0x2000, 0x206F),
    (0x20A0, 0x20C0), (0x2113, 0x2113), (0x2122, 0x2122), (0x2191, 0x2193), (0x2212, 0x2215),
    (0x2C60, 0x2C7F), (0xA720, 0xA7FF), (0xFEFF, 0xFEFF), (0xFFFD, 0xFFFD),
]

SCHNITTE = [
    ("RobotoFlex-VF.ttf", "Roboto Flex", 400, "Regular", "roboto-flex-regular.ttf"),
    ("RobotoFlex-VF.ttf", "Roboto Flex", 600, "SemiBold", "roboto-flex-semibold.ttf"),
    ("RobotoMono-VF.ttf", "Roboto Mono", 400, "Regular", "roboto-mono-regular.ttf"),
    ("RobotoMono-VF.ttf", "Roboto Mono", 600, "SemiBold", "roboto-mono-semibold.ttf"),
]


def schnitt(quelle, familie, gewicht, stil, datei):
    # Ohne neuen Zeitstempel: Derselbe Aufruf ergibt dieselben Bytes.
    schrift = TTFont(quelle, recalcTimestamp=False)
    # Alle übrigen Achsen (Breite, Neigung, optische Größe …) auf ihren Standardwert festlegen.
    lage = {achse.axisTag: None for achse in schrift["fvar"].axes}
    lage["wght"] = gewicht
    fest = instancer.instantiateVariableFont(schrift, lage, updateFontNames=False)

    optionen = subset.Options()
    optionen.layout_features = ["*"]
    optionen.name_IDs = ["*"]
    optionen.name_languages = ["*"]
    optionen.notdef_outline = True
    zuschnitt = subset.Subsetter(optionen)
    zuschnitt.populate(unicodes=[c for a, b in BEREICHE for c in range(a, b + 1)])
    zuschnitt.subset(fest)

    # Namen so setzen, dass WPF alle Schnitte unter einer Familie mit ihrem Gewicht führt:
    # Familie 16 ist die typografische, Familie 1 die alte mit dem Stil im Namen.
    namen = fest["name"]
    for kennung in (1, 2, 3, 4, 6, 16, 17, 21, 22, 25):
        namen.removeNames(nameID=kennung)
    alt = familie if stil == "Regular" else f"{familie} {stil}"
    postscript = familie.replace(" ", "") + "-" + stil
    for plattform, kodierung, sprache in ((3, 1, 0x409), (1, 0, 0)):
        namen.setName(alt, 1, plattform, kodierung, sprache)
        namen.setName("Regular", 2, plattform, kodierung, sprache)
        namen.setName(postscript, 3, plattform, kodierung, sprache)
        namen.setName(f"{familie} {stil}", 4, plattform, kodierung, sprache)
        namen.setName(postscript, 6, plattform, kodierung, sprache)
        namen.setName(familie, 16, plattform, kodierung, sprache)
        namen.setName(stil, 17, plattform, kodierung, sprache)
    fest["OS/2"].usWeightClass = gewicht
    # Nur das Bit REGULAR: Kursiv und Fett gibt es als eigene Achse nicht mehr.
    fest["OS/2"].fsSelection = (fest["OS/2"].fsSelection & ~0b1100001) | 0b1000000
    fest["head"].macStyle = 0
    fest.save(os.path.join(ZIEL, datei))


def main():
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    for quelle, familie, gewicht, stil, datei in SCHNITTE:
        schnitt(os.path.join(sys.argv[1], quelle), familie, gewicht, stil, datei)
        print(datei)


if __name__ == "__main__":
    main()
