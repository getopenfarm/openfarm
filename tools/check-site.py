"""Check local HTML links and assets in the assembled Pages site."""

from html.parser import HTMLParser
from pathlib import Path
from urllib.parse import unquote, urljoin, urlsplit

ROOT = Path(__file__).resolve().parents[1] / "site-dist"
ORIGIN = "https://getopenfarm.github.io"


class References(HTMLParser):
    def __init__(self):
        super().__init__()
        self.urls = []

    def handle_starttag(self, tag, attrs):
        for name, value in attrs:
            if value and (name == "href" or name == "src"):
                self.urls.append(value)


def exists(path):
    target = ROOT / unquote(path).lstrip("/")
    return target.is_file() or (target / "index.html").is_file() or target.with_suffix(".html").is_file()


errors = []
pages = list(ROOT.rglob("*.html"))
for required in ("index.html", "404.html", "docs/index.html", "docs/sitemap.xml", ".nojekyll"):
    if not (ROOT / required).is_file():
        errors.append(f"Missing required output: {required}")

for page in pages:
    relative = page.relative_to(ROOT).as_posix()
    page_url = ORIGIN + "/" + relative.removesuffix("index.html")
    parser = References()
    parser.feed(page.read_text(encoding="utf-8"))
    for reference in parser.urls:
        if reference.startswith("#"):
            continue
        url = urlsplit(urljoin(page_url, reference))
        if url.scheme in ("http", "https") and url.netloc == "getopenfarm.github.io":
            if not exists(url.path):
                errors.append(f"{relative}: missing {reference}")

if errors:
    raise SystemExit("\n".join(sorted(set(errors))))
print(f"Checked local links and assets in {len(pages)} HTML pages.")
