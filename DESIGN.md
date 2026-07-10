---
name: JIFAS Operations Console
description: Enterprise monitoring and AI admin dashboard for JIFAS Assistant
colors:
  primary: "#2454d6"
  primary-soft: "#eef2ff"
  green: "#15803d"
  green-soft: "#edf7f0"
  yellow: "#a16207"
  yellow-soft: "#fefce8"
  red: "#b91c1c"
  red-soft: "#fef2f2"
  cyan: "#0e7490"
  cyan-soft: "#ecfeff"
  neutral-bg: "#f0f2f5"
  surface: "#ffffff"
  surface-2: "#f8f9fb"
  surface-3: "#e8ebf0"
  text: "#111827"
  muted: "#6b7280"
  muted-2: "#9ca3af"
  border: "#dde1e9"
  border-strong: "#bcc3ce"
typography:
  display:
    fontFamily: "\"Segoe UI\", Arial, system-ui, sans-serif"
    fontWeight: 800
    fontSize: "34px"
    lineHeight: 1
    letterSpacing: "-0.025em"
  headline:
    fontFamily: "\"Segoe UI\", Arial, system-ui, sans-serif"
    fontWeight: 700
    fontSize: "16px"
    lineHeight: 1.25
    letterSpacing: "-0.01em"
  body:
    fontFamily: "\"Segoe UI\", Arial, system-ui, sans-serif"
    fontWeight: 400
    fontSize: "14px"
    lineHeight: 1.5
  label:
    fontFamily: "\"Segoe UI\", Arial, system-ui, sans-serif"
    fontWeight: 700
    fontSize: "10px"
    letterSpacing: "0.08em"
    textTransform: "uppercase"
  mono:
    fontFamily: "\"Cascadia Code\", \"Cascadia Mono\", Consolas, monospace"
    fontWeight: 400
    fontSize: "12px"
    lineHeight: 1.7
rounded:
  sm: "6px"
  md: "10px"
  pill: "999px"
spacing:
  sm: "8px"
  md: "16px"
  lg: "24px"
components:
  metric-card:
    backgroundColor: "{colors.surface}"
    borderRadius: "{rounded.md}"
    padding: "16px 18px"
    border: "1px solid {colors.border}"
  metric-card-primary-accent:
    backgroundColor: "{colors.surface}"
    borderRadius: "{rounded.md}"
    borderTop: "3px solid {colors.primary}"
  chip:
    backgroundColor: "{colors.surface-2}"
    borderRadius: "999px"
    padding: "4px 9px"
    fontSize: "11px"
    fontWeight: 700
  badge-ok:
    backgroundColor: "{colors.green-soft}"
    textColor: "{colors.green}"
    borderRadius: "999px"
    padding: "2px 7px"
    fontSize: "10px"
    fontWeight: 800
  badge-err:
    backgroundColor: "{colors.red-soft}"
    textColor: "{colors.red}"
    borderRadius: "999px"
    padding: "2px 7px"
    fontSize: "10px"
    fontWeight: 800
  badge-warn:
    backgroundColor: "{colors.yellow-soft}"
    textColor: "{colors.yellow}"
    borderRadius: "999px"
    padding: "2px 7px"
    fontSize: "10px"
    fontWeight: 800
  badge-type:
    backgroundColor: "{colors.primary-soft}"
    textColor: "{colors.primary}"
    borderRadius: "999px"
    padding: "2px 7px"
    fontSize: "10px"
    fontWeight: 800
  table-header:
    backgroundColor: "{colors.surface-2}"
    fontSize: "10px"
    fontWeight: 700
    letterSpacing: "0.06em"
    textTransform: "uppercase"
    color: "{colors.muted}"
---

# Design System: JIFAS Operations Console

## 1. Overview

**Creative North Star: "The Operations Console"**

JIFAS Operations Console adalah instrument panel enterprise-grade untuk monitoring chatbot AI dan administrasi. Setiap elemen visual ada karena data membutuhkannya — chrome nol, noise nol, signal maksimal. Angka harus bisa dibaca dalam 200ms oleh operator yang berdiri di depan layar atau duduk di meja panjang.

Emotional register: **controlled confidence**. Operator tidak pernah bertanya-tanya apakah sistem sehat atau tidak. Warnanya sudah pasti. Statusnya sudah jelas. Tidak ada ambiguity yang membutuhkan interpretasi.

Physical scene: ruang server kecil atau workstation IT di jam kerja sibuk. Operator sudah tahu apa yang mereka cari. Mereka butuh konfirmasi cepat, bukan tutorial.

**Key Characteristics:**
- Color-as-signal, bukan decoration. Warna carry information, bukan atmosphere.
- Information hierarchy absolute — metric paling penting di atas, detail di bawah.
- Zero-click insight — ringkasan kondisi langsung visible tanpa interaksi.
- Dense but breathable — banyak data, cukup spacing untuk scan cepat.
- Flat at rest. Shadows only on hover. Interaction is intentional.

## 2. Colors: The Corporate Blue Palette

**Warna adalah bahasa status, bukan estetika. Setiap saturasi earned.**

### Primary
- **Corporate Blue** (`#2454d6`): Aksi utama, link aktif, interactive focus. Bukan decoration. Kalau biru muncul, ada interaksi di situ.
- **Corporate Blue Soft** (`#eef2ff`): Background tint untuk elemen yang sedang aktif atau selected. Tidak muncul di tempat statis.

### Semantic Status
- **Success Green** (`#15803d` / `#edf7f0`): Request sukses, cache hit, sistem OK. Pasangan: badge OK, chip ok, bar hijau.
- **Warning Amber** (`#a16207` / `#fefce8`): Perlu perhatian, latency tinggi, cache miss. Pasangan: badge warning, chip warn.
- **Error Red** (`#b91c1c` / `#fef2f2`): Error rate, request gagal, alarm. Pasangan: badge error, status disconnected.
- **Info Cyan** (`#0e7490` / `#ecfeff`): Cache hit indicator di tabel, chip cache. Memisahkan cache dari data biasa secara visual.

### Neutral
- **Neutral Background** (`#f0f2f5`): Body background. Tidak tinted warm atau cool — netral grey-blue enterprise.
- **Surface** (`#ffffff`): Card, panel, overlay. Maximum contrast dengan background.
- **Surface 2** (`#f8f9fb`): Table header, input background, chip default.
- **Text Primary** (`#111827`): Semua teks yang perlu dibaca. Bukan grey-400 — contrast ratio harus 4.5:1 minimum.
- **Muted** (`#6b7280`): Label, subtitle, footnote. Tetap readable.
- **Muted 2** (`#9ca3af`): Placeholder, disabled, border muted.
- **Border** (`#dde1e9`): Card edge, dividers, table row.
- **Border Strong** (`#bcc3ce`): Hover state border, input focus ring.

### Named Rules
**The Signal Doctrine.** Warna hanya muncul di tiga konteks: status (OK/err/warn), interactive (button, link, focus), dan semantic (cache=cyan). Tidak ada fourth use case. Warna yang tidak signal adalah noise.

**The Muted Ceiling.** `--muted` tidak pernah turun dari `#6b7280` untuk body text. Placeholder text harus tetap 4.5:1 contrast. Light grey untuk elegance adalah anti-pattern yang membunuh readability.

## 3. Typography

**Single family. System UI stack. Numbers in mono.**

**Font:** Segoe UI (primary) / Arial / system-ui fallback. No Google Fonts. No custom font loading. Load time nol.

**Mono Font:** Cascadia Code / Cascadia Mono / Consolas untuk timestamp, token count, duration, correlation ID.

**Character:** Neutral enterprise sans. Tidak fashionable, tidak distinctive. Berfungsi. Operator tidak notice font — mereka notice data.

### Hierarchy
- **Display** (800 weight, 34px, -0.025em tracking, line-height 1): Metric KPI values — total request, latency, error rate. Numerik. Besar. Tabular-nums variant.
- **Headline** (700 weight, 16px, -0.01em tracking, line-height 1.25): Section headings, card titles.
- **Body** (400 weight, 14px, line-height 1.5): Paragraph text, descriptions.
- **Label** (700 weight, 10px, 0.08em tracking, uppercase): Table header, metric label, chip. Purpose-built untuk scan.
- **Mono** (400 weight, 12px, line-height 1.7): Data cells — waktu, token, durasi, ID.

### Named Rules
**The Mono for Data Rule.** Semua field numerik yang bukan metric card (timestamp, token count, duration dalam tabel) wajib menggunakan font mono. Bukan stylistic choice — ini supaya angka align dan mudah discan.

## 4. Elevation

**Flat at rest. Shadow is interaction signal.**

Shadow vocabulary minimal: tiga level untuk hover response, tidak lebih.

### Shadow Vocabulary
- **Hover micro-lift** (`0 1px 3px rgba(15, 23, 42, 0.06)`): Card hover. Menandakan element bisa interacted.
- **Panel lift** (`0 4px 16px rgba(15, 23, 42, 0.08)`): Panel yang sedang aktif di foreground.
- **Overlay lift** (`0 8px 32px rgba(15, 23, 42, 0.1)`): Auth overlay, modal-level elements.

### Named Rules
**The Flat-By-Default Rule.** Semua surface flat saat render. Shadow hanya muncul pada hover atau elevation change. Tidak ada ambient shadow. Tidak ada decorative shadow. Shadow = interaction signal.

## 5. Components

### Metric Cards
- **Corner Style:** 10px radius
- **Background:** Surface white
- **Shadow Strategy:** `shadow-sm` on hover only (Flat-by-Default Rule)
- **Border:** 1px solid `--border`
- **Top accent:** 3px colored top border untuk semantic card type (primary=blue, green=green, red=red, none=default)
- **Internal Padding:** 16px horizontal, content-driven vertical
- **States:** Default (flat), Hover (shadow-sm + border-strong), Alarm (red border, red value, alarm class)

### Chips
- **Style:** Pill shape (border-radius 999px), 4px 9px padding, 11px bold
- **Default:** `--surface-2` background, `--border` border, `--muted` text
- **OK:** `--green-soft` background, `--green` border (transparent), `--green` text
- **Warn:** `--yellow-soft` background, `--yellow` text
- **Err:** `--red-soft` background, `--red` text

### Badges
- **Style:** Pill shape, 2px 7px padding, 10px 800 weight. Maximum density.
- **OK/Err/Warn:** Semantic colors yang sama dengan chip, tapi lebih saturated (soft background, bold text).
- **Type badge:** `--primary-soft` background, `--primary` text. Untuk call type label di tabel.

### Table
- **Header:** `--surface-2` background, 10px uppercase bold label. Sticky top.
- **Row:** Alternating `--surface` dan `color-mix(surface-2 45%, surface)`. Cache row: `color-mix(cyan-soft 30%, surface)`.
- **Hover:** `--row-hover` (semi-transparent primary tint).
- **Cell text:** Body untuk user/module/type, mono untuk numerik. Right-align numerik.

### Auth Overlay
- **Background:** `--bg` full viewport. Blur backdrop tidak dipakai.
- **Card:** Surface white, radius 10px, shadow-lg, 36px 40px padding.
- **Input:** `--surface-2` background, `--border` border, 10px 14px padding.
- **Button:** Full-width, `--primary` background, white text.

### Live Feed
- **Background:** `--surface-3`. Membedakan dari panel content biasa.
- **Font:** Mono. 11px. Line-height 1.7.
- **Content:** Timestamps in primary blue, OK in green, ERROR in red, muted untuk separator.

### Status Badge (Header)
- **Connected:** Green soft background, green text, green border. Dot indicator animates with `pulse-red` keyframe.
- **Disconnected:** Red soft background, red text, red border. Dot pulses.

## 6. Do's and Don'ts

### Do:
- **Do** use color exclusively for status signal. Blue = interactive. Green = OK. Red = error. Amber = warning. Cyan = cache.
- **Do** use tabular-nums (`font-variant-numeric: tabular-nums`) pada semua metric value dan monospace cells.
- **Do** keep metric values large and bold (34px display) — angka di dashboard harus bisa dibaca dari 2 meter.
- **Do** use right-align for numeric table columns. Alignment = scanability.
- **Do** use 999px border-radius untuk chip/badge/pill — soft, tidak sharp.
- **Do** implement dark mode toggle. Operator bekerja dalam berbagai kondisi cahaya.

### Don't:
- **Don't** use `--muted` below `#6b7280` for body text. Light grey "for elegance" fails WCAG 4.5:1 contrast.
- **Don't** use decorative shadow. Shadow is only for hover response and overlay.
- **Don't** use gradient backgrounds anywhere. Surface is solid.
- **Don't** use gradient text. Single solid color for all headings and labels.
- **Don't** use glassmorphism. Backdrop blur is not used.
- **Don't** use emoji in data labels. Status should be conveyed through color/badge, not icons.
- **Don't** use tooltip untuk informasi kritis. Semua data harus visible tanpa hover.
- **Don't** mix font families. Segoe UI everywhere, mono for data. One family, one mono.
- **Don't** animate layout properties. Only transform and opacity for transitions.
