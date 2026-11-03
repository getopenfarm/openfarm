"""Check links, crawlability, and homepage SEO in the assembled Pages site."""

import json
from html.parser import HTMLParser
from pathlib import Path
from urllib.parse import unquote, urljoin, urlsplit
from xml.etree import ElementTree

ROOT = Path(__file__).resolve().parents[1] / "site-dist"
ORIGIN = "https://getopenfarm.github.io"


class References(HTMLParser):
    def __init__(self):
        super().__init__()
        self.urls = []
        self.meta = {}
        self.canonicals = []
        self.h1_count = 0
        self.structured_data = []
        self.json_script = False

    def handle_starttag(self, tag, attrs):
        attributes = dict(attrs)
        if tag == "meta":
            self.meta[attributes.get("name", attributes.get("property"))] = attributes.get("content")
        if tag == "link" and attributes.get("rel") == "canonical":
            self.canonicals.append(attributes.get("href"))
        if tag == "h1":
            self.h1_count += 1
        if tag == "script" and attributes.get("type") == "application/ld+json":
            self.json_script = True
            self.structured_data.append("")
        for name, value in attrs:
            if value and (name == "href" or name == "src"):
                self.urls.append(value)

    def handle_data(self, data):
        if self.json_script:
            self.structured_data[-1] += data

    def handle_endtag(self, tag):
        if tag == "script":
            self.json_script = False


def exists(path):
    target = ROOT / unquote(path).lstrip("/")
    return target.is_file() or (target / "index.html").is_file() or target.with_suffix(".html").is_file()


errors = []
pages = list(ROOT.rglob("*.html"))
for required in ("index.html", "404.html", "docs/index.html", "docs/sitemap.xml", "app-preview/index.html", ".nojekyll", "robots.txt", "sitemap.xml", "sitemap-pages.xml", "social-card.png"):
    if not (ROOT / required).is_file():
        errors.append(f"Missing required output: {required}")

for page in pages:
    relative = page.relative_to(ROOT).as_posix()
    page_url = ORIGIN + "/" + relative.removesuffix("index.html")
    parser = References()
    parser.feed(page.read_text(encoding="utf-8"))
    if relative == "index.html":
        if parser.h1_count != 1 or parser.canonicals != [ORIGIN + "/"]:
            errors.append("Homepage must have one H1 and one canonical root URL")
        for name in ("description", "og:title", "og:description", "og:image", "twitter:card", "twitter:image"):
            if not parser.meta.get(name):
                errors.append(f"Homepage is missing {name} metadata")
        if "noindex" in (parser.meta.get("robots") or ""):
            errors.append("Homepage must be indexable")
        try:
            if not parser.structured_data:
                raise ValueError("No homepage structured data")
            for raw in parser.structured_data:
                data = json.loads(raw)
                if data.get("@context") != "https://schema.org":
                    raise ValueError("Unexpected structured data context")
        except (ValueError, TypeError) as error:
            errors.append(f"Invalid homepage structured data: {error}")
    if relative == "404.html" and "noindex" not in (parser.meta.get("robots") or ""):
        errors.append("Website error page must be marked noindex")
    if relative == "app-preview/index.html" and "noindex" not in (parser.meta.get("robots") or ""):
        errors.append("The live demo must be marked noindex")
    for reference in parser.urls:
        if reference.startswith("#"):
            continue
        url = urlsplit(urljoin(page_url, reference))
        if url.scheme in ("http", "https") and url.netloc == "getopenfarm.github.io":
            if not exists(url.path):
                errors.append(f"{relative}: missing {reference}")

namespace = {"sm": "http://www.sitemaps.org/schemas/sitemap/0.9"}
for relative in ("sitemap.xml", "sitemap-pages.xml", "docs/sitemap.xml"):
    try:
        tree = ElementTree.parse(ROOT / relative)
        locations = tree.findall(".//sm:loc", namespace)
        if not locations:
            errors.append(f"Empty sitemap: {relative}")
        for location in locations:
            url = urlsplit(location.text or "")
            if url.scheme != "https" or url.netloc != "getopenfarm.github.io" or not exists(url.path):
                errors.append(f"{relative}: invalid or missing URL {location.text}")
            if "404" in url.path or url.path.startswith("/app-preview"):
                errors.append(f"{relative}: {url.path} must not appear in sitemap")
    except (OSError, ElementTree.ParseError) as error:
        errors.append(f"Invalid sitemap {relative}: {error}")

robots = ROOT / "robots.txt"
if robots.is_file() and f"Sitemap: {ORIGIN}/sitemap.xml" not in robots.read_text():
    errors.append("robots.txt must advertise the combined sitemap index")

if errors:
    raise SystemExit("\n".join(sorted(set(errors))))
print(f"Checked links and assets in {len(pages)} HTML pages, homepage SEO, robots.txt, and website/docs sitemaps.")
