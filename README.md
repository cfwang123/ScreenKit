# ScreenKit

Windows desktop tool (`ScreenKit.exe`, window title ScreenKit): screenshot recognition, annotation, recording, speech, translation, a dictionary, and face, plus file transfer and screencast with the Android companion. This PC also has an HTTP API and a browser tools page.

**Current version: 1.0.28** · [Releases](https://github.com/cfwang123/ScreenKit/releases/latest) · [Changelog](CHANGELOG.md) · [中文](README.zh.md)

![Main window](docs/1%20screenshot.en.png)

## Screenshot and recognition

- **Screenshot OCR**: A hotkey or the menu selects a region and recognizes text and barcodes; the recognition engine can be switched, and recognition can overlay a translation.
- **Screenshot annotate**: Rectangle, ellipse, arrow, pen, and text; on finish it copies as an image, a file, or a path.
- **Repeat last capture**: A hotkey or the menu grabs the last region you selected again.
- **Long screenshot**: Click a scrollable window and it scrolls and stitches one long picture.
- **Screenshot history**: Browse saved pictures as thumbnails and pan, zoom, annotate, or copy them. Pictures older than the retention period are deleted in the background at startup and every 24 hours after that.
- **Overlay text**: Draws the recognized text over the picture to select from.
- **Clipboard**: Paste a picture and recognize, annotate, or save it.
- **PDF workbench**: Open a PDF, recognize it page by page, edit the text, and export a searchable PDF.

## Recording

**Capture → Screen record** or **GIF record**: click a window, or drag a region.

- The red frame is drawn outside the selection; size and aspect can be set before start, and the control bar can be dragged and collapsed. The audio source can be changed while recording.
- **Region recording**: Writes an MP4, with system audio, the microphone, or both, and optional mouse and click highlights.
- **GIF record**: Uses the same region capture and writes a silent GIF, with a frame rate and a palette.
- **Record options**: The frame rate and the same shorter-side cap as screenshots.

## Speech, translation, dictionary, and face

- **Speech recognition**: Live captions with an offline or streaming model.
- **Speech synthesis**: Windows speech, SAPI, offline speech, Edge natural voices online, and voices in the local Chrome. Chrome lists only installed languages. Chrome model installation opens Chrome with this app's private profile on the model page. An installed voice can play a short test line. **Settings → Speech synthesis** is a per-language table of engine, voice, and rate. Chinese, Japanese, Korean, and English always stay; other languages can be added or removed. Dictionary entry speech, and Read aloud in the selection popup when the engine is Auto, use that row.
- **Voice input**: The hotkey starts and finishes it, and the result goes to the current input position.
- **Chat**: Uses a configured LLM, with bubbles, microphone input, and speaking.
- **LLM log**: The latest ~1,000 requests with the model, elapsed time, and the request and response.
- **Translation**: Local ONNX, a configured LLM, or the local Chrome. Auto mode translates between the native and learning languages and shows the detected direction; the tab and popup pick the engine and model. Chrome language lists show only installed packs, and Chrome model installation opens the same private profile on the model page.
- **Face**: Detection and comparison, with optional landmarks and gender and age.
- **Chinese to pinyin**: Full spelling without tone marks, or one lowercase initial per character.
- **Wubi, Japanese, and Korean IME**: Type with a candidate bar in this app and in other programs. Japanese candidates include the dictionary kanji spellings, plus verb and i-adjective conjugations. A partial reading also lists every word that can still be completed. An old lexicon file is marked as a new version under **Help → Install features**, where it can be reinstalled or removed. Turn it on from **Tools**, switch languages from the tray; set hotkeys, enable languages and manage the lexicon in **Settings → Input method** (disabled languages are skipped by the cycle hotkey).
- **Simplified/Traditional**: Simplified on top, traditional below, following Taiwan character forms.
- **Japanese yomi**: Turns a Japanese sentence into readings, by word or by character.
- **Calendars**: Gregorian to lunar (sexagenary cycle and leap months), plus Japanese, Taiwan, Hebrew, Hijri, Persian, and others.

### Dictionary

The main window, or **Tools → Dictionary**. Look up Chinese, English, Japanese, and Korean.

- Language filter: All, Chinese, Japanese, Korean, English.
- Pure Latin input is romanized for Japanese and Korean search (romaji to hiragana, Korean romanization to Hangul), such as `watashi` and `hanguk`.
- A hit has pronunciation, part of speech, senses, phrases, and examples, all of which can be spoken; **Export** writes every hit of this search.
- Double-click, drag, or right-click inside an entry to pop up the lookup window, up to 100 hits. Dictionary entries always do this.
- **Settings → Dictionary** sets how many hits to list, how long to keep the database open, and the selection popup.
- The dictionary hotkey is empty by default: it brings the window forward on the dictionary when it is not the main window, and hides the main window when it is.
- The web dictionary is its own page at `/dict`, opened from a card under Text on the tools page; Ctrl+click opens it in a new window. It has the same search, four languages, speech, and export.
- **Selection popup** is off by default and can be turned on on the dictionary page, under **Settings → Dictionary**, or on the tray. While it is on, dragging or double-clicking text in this program or another shows a lookup popup that does not take focus. With no hits the window stays the same size as when it has hits. A selection with punctuation a word cannot contain, or longer than a word, opens the popup without querying the dictionary. **Translate** on the right defaults both languages to Auto: native text goes to the learning language, anything else goes to the native language. After a manual language change, click Translate once. The source text can be edited, and the resolved source and target languages are shown beside it. Switching dictionary language or translating does not change the popup width. Drag the lower-right corner to resize, or drag an empty area to move it. The size is saved when you quit and restored next time. **Read aloud** speaks the text sentence by sentence and highlights the sentence being spoken, with a choice of engine (including Chrome), speed, language, and voice. Auto uses that language's engine, voice, and rate from **Settings → Speech synthesis**. While that tab stays open the popup does not dismiss itself and ignores new selections.

## Files and screencast

### File sync

On the PC, open **File sync**; the phone and the PC are on the same LAN.

- The list is `sendfile/` next to the program, one level deep: list or thumbnails, marquee and multi-select, cut/copy/paste, and delete to the Recycle Bin.
- Dropping files onto the list puts them in the inbox; dropping onto the lower area sends them to the phone while it is connected, and a phone share or upload writes to `sendfile/`.
- **Web manager** uses the same port: the PC opens `/files` and the phone `/m`, where a login allows uploads, folders, rename, and delete.

### Screencast

This program receives while it is running, and that can be turned off in Settings; **Tray → Tools → Screencast** opens the screencast page. Use the same Wi-Fi, with guest isolation off.

- **PC to PC**: Pick a saved PC, scan the LAN, or type an IP; the picture is kept within 1000×1000 by default.
- **Phone to PC**: Pick a saved PC or scan the LAN in the phone app, over the LAN or a USB cable (debugging or accessory).
- The viewer is not pinned on top, the picture can fit the window or fill it, closing it disconnects, and quality and the audio source can be changed while casting.

## Tools

- **Image convert**: Batch convert to JPG, PNG, palette PNG, or BMP, with a shorter-side cap, rotation, and mirror.
- **Batch video convert**: Batch convert to MP4, MKV, or WebM, with a shorter-side cap and the audio kept.
- **Video to GIF**: Pick a start and an end and write a silent GIF, with a frame rate and a palette.
- **Character art**: The **Mode** list picks Shades ASCII, Dot shading, or one of the line drawings; full-color character art is drawn in a console window.
- **Pixel art**: Choose a width and a height, paint on large cells, and preview at real pixel size. Copy or save a PNG.
- **Character bitmaps**: Draw each character as a dot matrix and export a C array, hex, or dot-matrix text.
- **QR / barcode**: Generate QR, Data Matrix, Code 128/39, EAN, and UPC.
- **Batch rename**: Edit target names by hand or with rules such as `%1` and `#`, and sort by name or date.
- **Fast copy**: Copy, cut, delete to the Recycle Bin, or make a symbolic or hard link, and register an Explorer cascade menu.
- **Hash**: MD5, SHA-1, and SHA-256. Paste an expected value to compare.
- **Text tools**: Base64, URL, hex, escapes, case, whitespace, counts, JSON pretty-print, and more.
- **Markdown reader**: Open or drop a Markdown file (or a shortcut to one) and read it with the IE11 engine built into Windows; switch between light, dark, sepia, eye care, and midnight themes, click an image to view it full size, and export it to Word (needs Word installed) or PDF (printed by Edge/Chrome).
- **Word/PDF to MD**: Convert a .docx or a text PDF to Markdown, then preview, copy, or save it. Images are extracted into the chosen folder (default `images/001.png`; `{name}` becomes the document name), and PDF text comes from the installed PDFium; scanned pages need PDF recognition first.
- **Password generator**: Generate passwords from a length and character classes, or hand a sentence to a configured LLM and spell the translation out.
- **Network**: Diagnostics, IP scan, and port scan, covering Ping, DNS, Wake-on-LAN, trace route, and more.
- **Ports in use**: Lists the TCP and UDP ports in use and the process behind each, and can open that folder.
- **System info**: System, USB, serial ports, printers, sound, fonts, displays, cameras, Bluetooth, disks, and other devices.
- **System tools**: Read-only lists of processes, environment variables, startup items, installed programs, services, hotfixes, and Store apps, plus file locks and file associations.
- **Performance**: A borderless window with CPU, GPU, memory, network, and disk, with a pin and an update interval.
- **File info**: Reads the header type and shows version, image, media, and text-encoding details. EXE and DLL files get another page for sections, imports, exports, the signature, and public .NET types.
- **HTTP request debugger**: Requests are saved in groups; parameters are one `key=value` per line, and a POST can be JSON, XML, or raw text.
- **MIDI synthesizer**: Play with the mouse or the keyboard, open, change, and save MIDI files, and see a live waveform.
- **Sound recorder**: Record a microphone, system sound, or both, then play it or save a WAV.
- **Camera**: Preview a camera, take photos and videos, and scan QR codes and barcodes.
- **Scientific calculator**: A borderless keypad calculator with trigonometry, logarithms, powers, roots, factorial, and memory.
- **Desktop note**: A borderless translucent note for text or a to-do list; content and position are remembered. The pin in the title bar keeps it above other windows, and that too is remembered.
- **RTSP test server**: Encodes a bouncing picture to H.264 and serves an RTSP address on the chosen port.
- **Auto key**: Sends keys or clicks in a row to the foreground window; the default F6 starts and stops it while the window is open.
- **Window manager**: Lists visible top-level windows and pins one in front or unpins it.

### Web tools

- **Text**
  - **Text convert**: Encoding, escaping, and common text transforms
  - **Chinese to pinyin**: Characters to pinyin or initials
  - **Markdown reader**: Preview Markdown, with theme switching and click-to-zoom images
  - **Novel reader**: A separate page (`/reader`) like the dictionary, opened from a card under Text on the tools page; Ctrl+click opens it in a new window. Drop or add txt/md files; they stay in this browser. Select text for the dictionary popup (drag its edges or corners to resize, or maximize it). It reads with the current browser’s own voices by default, and can switch to Chrome offline or other voices. Reading starts at the top of the screen or at the selected word and highlights the current sentence. Google voices do not speak online; a missing offline pack is installed when reading starts, with a progress dialog that can be cancelled, and each voice shows whether that pack is installed. Choosing browser voices shows an Install models link
  - **Chinese segmentation**: Split by word, sentence, or grapheme
  - **Japanese conjugation**: Enter the dictionary form, or pick a common verb, adjective, or noun from the input’s dropdown. Auto first checks the Japanese dictionary to tell a verb from an i-adjective, na-adjective, or noun, and the type can be chosen by hand. Verbs are listed by class (1, 2, or 3) with common auxiliaries (ながら, やがる, ている and so on), polite forms (ましょう, ませんか), honorific, humble and deferential forms, followed by Kansai, Kyushu, Tohoku, and Hiroshima forms that differ from standard Japanese; adjectives and nouns list negative, past, te, ba, naru and other forms. Each form has a speak button. Without the dictionary it guesses from the ending: る is class 2, する and くる are class 3, い is an i-adjective, anything else is a noun. いく uses って / った. ある’s negative is ない. 問う uses うて / うた. なさる uses います. くれる’s imperative is くれ. いい and かっこいい conjugate on よ.
  - **Korean verbs**: Pick a type (verb, adjective or noun), then type a word or choose one from the list. Conjugations are labeled by speech level such as formal, polite, or casual, and a noun is conjugated with 이다. Each form has a speaker button. Auxiliary verbs (try, finish off, leave done, do for someone), causative, passive and change forms, the 하오체 and 하게체 styles, and common connectives are included. Usages that differ from standard Korean are grouped by Gyeongsang, Jeolla, Chungcheong, Jeju, Hamgyeong, and Pyeongan dialect
  - **English word forms**: Pick a type (auto, verb, adjective or noun), then type a word or choose one from the list. Auto checks the irregular verb, adjective and noun tables, then the ending; if unsure it treats the word as a verb. Verbs list the basic forms, the sixteen tenses, negatives, questions, passive, modals, subjunctive, imperative and requests, tag questions, and causative, plus British, American and Australian/NZ differences such as -t/-ed, gotten/got, dove/dived and doubled -l. Irregular pasts and participles such as went or gone are recognized, and phrasal verbs can carry a particle. Adjectives list comparative and superlative forms, the spelling rule, and patterns such as as ... as, than and the most, with the adverb and noun. Nouns list plural, possessives, a / an, countability, agreement, and patterns such as there be and this / these; irregular plurals and uncountable nouns are marked. Each form has a speak button
  - **Russian verb conjugation**: Enter an infinitive, optionally with -ся. The table lists aspect, present or future, past, imperative, subjunctive, active and passive participles (with short forms), and gerunds. Perfective verbs have no present, so their finite forms are future. Aspect is inferred from the ending and prefix and can also be set by hand. Each form has a speak button
  - **In-browser model**: Run text tasks on the browser’s local model; Speak can filter voices by language. Google voices use the installed offline pack, and the list shows whether that pack is installed. The page link opens the Chrome model page
  - **Translate**: Translate text into another language, with its own engine and local ONNX model pickers
  - **Password generator**: Generate passwords
  - **Regular expressions**: Find regex matches
  - **Number names**: Write a number in several readings. Past ten thousand digits, if the large-number scale still applies, only that reading and scientific notation are shown. That scale repeats 无量大数 and takes exponents up to nine digits. Finite values also show FP64, FP32, FP16, BF16, and FP8 as hex and a*2^x
  - **Test data**: Generate fake data by type
- **Math**
  - **Scientific calculator**: Scientific calculation
  - **Radix conversion**: Convert among base 2, 8, 10, and 16
  - **Unit conversion**: Length, weight, currency, and more
  - **Random number**: A number inside a chosen range
  - **Function graph**: Plot 2D curves, 3D surfaces, and implicit equations such as x^2+y^2+z^2=9 for a sphere, with built-in examples
  - **BMI**: Body mass index from height and weight
  - **Mortgage**: Equal payment or equal principal
  - **Compound interest**: Interest compounded yearly
- **Speech**
  - **Speech**: Read text aloud
  - **Speech recognition**: Turn speech into text
  - **8-bit synthesizer**: Square-wave tones and effects, octave -/+ buttons (C1-C7), and a live waveform
- **Calendar**
  - **Calendars**: Gregorian alongside lunar and other calendars
  - **Timestamp**: Unix time and local time
  - **Date arithmetic**: Difference or offset between dates
  - **Timer**: Stopwatch and countdown
  - **World clock**: Local time in other places
  - **Workdays**: Move a date by workdays
- **Image**
  - **Text recognition**: Read text in a picture
  - **QR / barcode**: Make a QR code or barcode
  - **Scan QR / barcode**: Read a code from a picture
  - **Camera**: Take a photo or scan a code
  - **RTSP player**: Play an RTSP video
  - **Image Base64**: Picture and Base64
  - **Image to ICO**: Turn a picture into an icon
  - **Color picker**: Pick or sample a color
  - **CSS gradient**: Preview a CSS gradient
  - **Drawing board**: Draw on the page
  - **HTML preview**: Write HTML and see it
  - **Placeholder image**: Make a placeholder picture
  - **Random colors**: A set of random colors
- **Lookup**
  - **Color chart**: Named colors
  - **HTTP status codes**: Status codes, descriptions, and specs by 1xx–5xx, plus common non-standard ones
  - **MIME types**: MIME types, file extensions, and Content-Type examples
  - **Common ports**: TCP/UDP ports, services, and what they are used for
  - **File signatures**: Identify the real format from a file's hex header
  - **Time zones**: IANA zones, abbreviations, and standard/DST offsets
  - **Country, language & currency codes**: ISO codes for countries, languages, and currencies
  - **English irregular verbs**: Base, past, and past participle; search by any form or meaning, and filter by AAA, ABB, and other patterns
  - **Key codes**: A key's event.code, event.key, keyCode, and Windows VK
  - **Linux commands**: Commands by file, package, process, network, and more, each shown right in the list with a commented multi-line example, plus Bash syntax help; switch between Ubuntu, Debian, Deepin/UOS, RHEL, Fedora, openEuler, Arch, openSUSE, and Alpine, and the last choice is remembered
  - **Windows commands**: Windows PowerShell, pwsh, and CMD commands by category, each shown right in the list with a commented multi-line example, plus a syntax-help group for all three shells; the last shell is remembered
  - **SQL functions**: Look up functions and syntax for MySQL, SQL Server, Oracle, SQLite, and PostgreSQL, grouped by strings, numbers, dates, casts, aggregation, window functions, JSON, and metadata; the last database is remembered
  - **Linux software**: Open-source Linux packages and install commands for common apps
  - **This browser**: What the browser can read about this device
  - **USB IDs**: USB vendor and product
  - **Unicode chart**: Unicode characters
  - **ASCII chart**: ASCII characters
  - **GBK chart**: GBK characters
  - **Morse code**: Morse code
- **Talk**
  - **Chat room**: Send text, pictures, and files
- **Files**
  - **File transfer**: Move files between the phone and this PC on the same LAN

## Hotkeys

They can be changed in Settings. An empty hotkey string turns that hotkey off.

| Hotkey | Action |
|--------|--------|
| `Ctrl+Alt+O` | Show or hide the main window |
| `Ctrl+Alt+Q` | Screenshot annotate |
| `Ctrl+Alt+G` | Repeat last capture region |
| `Ctrl+Alt+W` | Screenshot and recognize |
| `Ctrl+Alt+V` | Voice input; press again to finish |
| `Ctrl+Alt+B` | Live captions |
| `Ctrl+Alt+T` | Show or hide the translate popup |
| `Ctrl+Alt+P` | Cycle the screenshot copy mode: image, file, path |
| `Ctrl+Alt+H` | Open the web tools home in the browser |

## Install, language, and updates

The slim package does not include models or the large runtimes; `7za.dll` is bundled to extract 7z archives. Screenshot, annotation, recording, Windows OCR, system speech, most tools, file transfer, and the PC side of screencast work right after install; Windows OCR and system speech also need that language in the operating system.

Barcode, ONNX text recognition, offline speech, face, local Chinese–English translation, the dictionary, the input method, PDF, FFmpeg, CUDA, and DirectML are installed or removed under **Help → Install features**, and using a component that is not installed yet shows a prompt.

**Settings → General** can turn a group of features off. The UI language is switched on the menu bar or on that same page: Chinese, English, Japanese, Korean, German, Spanish, French, Portuguese, and Russian. **Help** holds Check for updates, Diagnostics, and Memory. A newer version lists the notes for each release since the one you have; pick a version to read that one, or read them all.

## Run

Windows 10/11 (x64). It needs [.NET Framework 4.8](https://dotnet.microsoft.com/download/dotnet-framework/net48).

The local HTTP port defaults to 1224; after **Allow LAN access** is checked, the same LAN can connect. File transfer, the web tools, and screencast share this port, and `POST /api/ocr` matches Umi-OCR text recognition. If that port cannot be opened, or the HTTP switch is off, a random port that accepts only this computer still serves the tools pages and the HTTP API, and menus and buttons open that port.

## License

Application source in this repository is under the **MIT License**. The full text is [LICENSE](LICENSE). Copyright (c) 2026 ScreenKit Contributors. Under the MIT terms you may use, copy, modify, merge, publish, distribute, sublicense, and sell it. The software is provided “as is”, with no warranty, express or implied.

The items below do not automatically use this repository’s MIT license. Each license is in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

- **ONNX Runtime**, **PDFsharp**, **PDFtoImage**, **NAudio**, **SharpCompress**, **Vortice**, and **Bogus** are MIT. PDFtoImage also brings the Pdfium and Skia native libraries.
- **OpenCV** (OpenCvSharp) and **ZXing** are Apache-2.0.
- **7za.dll** (7-Zip) is LGPL-2.1 or later. The license text is `License.7za.txt` next to the program.
- **FFmpeg**: the binding code is LGPL-3.0. The `ffmpeg64` build itself is GPL or LGPL, depending on that build. Redistribution has to follow those terms.
- **DirectML** and **CUDA / cuDNN** follow Microsoft’s and NVIDIA’s redistribution terms.
- **ONNX models** such as text recognition are usually Apache-2.0, or the terms of that model pack’s author.
- **NPinyin** turns Chinese into pinyin. The package ships with no license file.
- **SQLite** (`e_sqlite3.dll`) opens the input-method database. It is in the public domain.

`dict3.bin` keeps Chinese, Japanese, Korean, and English in one file. ScreenKit does not build this file. Install features downloads it. Inside it:

- **Chinese.** Characters, words, and idioms come from [mapull/chinese-dictionary](https://github.com/mapull/chinese-dictionary) (a pinyin dictionary). The license is MIT. The upstream notes say the origin of some material is unclear.
- **Japanese.** Word senses come from [JMdict/EDICT](https://www.edrdg.org/wiki/index.php/JMdict-EDICT_Dictionary_Project) (EDRDG). The license is [CC BY-SA 4.0](https://www.edrdg.org/edrdg/license.html). Redistribution has to keep the EDRDG attribution and follow that project’s update terms.
- **Korean.** A derivative of the National Institute of Korean Language dictionary. The license is CC BY-SA 2.0 KR.

Step-by-step use, the API, and the build are in the [full guide](docs/archive/README.md). The phone app is in [android/README.md](android/README.md).
