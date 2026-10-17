# ScreenKit

Windows desktop tool (`ScreenKit.exe`, window title ScreenKit): screenshot recognition, annotation, recording, speech, translation, a dictionary, and face, plus file transfer and screencast with the Android companion. This PC also has an HTTP API and a browser tools page.

**Current version: 1.0.23.1** · [Releases](https://github.com/cfwang123/ScreenKit/releases/latest) · [Changelog](CHANGELOG.md) · [中文](README.zh.md)

![Main window](docs/1%20screenshot.en.png)

## Screenshot and recognition

- **Screenshot OCR**: A hotkey or the menu selects a region. The result area splits into OCR and barcode. With no other model chosen, it uses Windows OCR, loads no ONNX model, and does not need OpenCV. After an ONNX model is installed, the engine can be changed. Korean and English put spaces back from the gaps between words on the picture. Windows OCR does the same for English words when Chinese or English is selected: a glued word is split where the picture has a word gap. Chinese+English runs both languages and keeps Chinese characters from the Chinese pass and Latin text from the English pass. When a target language is selected and an LLM is configured, recognition can overlay a translation. On multiple monitors, the frame is drawn on every screen the selection covers.
- **Screenshot annotate**: Rectangle, ellipse, arrow, pen, and text, or no tool. The next region select keeps the last tool. On finish it can copy as an image, a file, or a path, and it remembers that choice. `P` on the annotate bar only switches the copy mode. It does not finish this shot.
- **Repeat last capture**: A hotkey (default `Ctrl+Alt+G`, can be cleared) or **Screenshot → Repeat last capture** / the tray item grabs the last region you selected (after at least one region capture). It copies as an image and shows it on the **Screenshot OCR** tab without forcing the main window to the front.
- **Long screenshot**: Click a scrollable window. It scrolls and stitches the picture, then writes the clipboard with the current copy mode. It does not recognize text, does not put the picture on the main window, and does not need OpenCV.
- **Screenshot history**: Thumbnails of the pictures in the save folder. Pan, zoom, and annotate. With marks, Done, OCR, or Open with the system saves a new file. With no marks it uses the original. Copy puts the picture on the clipboard and leaves the window open.
- **Overlay text**: Glyphs stay at their normal shape and match the line height. A short line leaves the rest of the box blank. The region is fixed when the button is released. A drag selection does not grow on its own. Ctrl+C copies. A click on empty space cancels.
- **Clipboard**: Paste a picture and recognize it. Right-click the picture to paste, open history, copy as an image / file / path, save, open with the system, annotate again, or recognize text or a barcode. Switching the copy mode from the menu, the tray, or `Ctrl+Alt+P` writes the last screenshot again in the new mode at once.
- **PDF workbench**: **Tools → PDF workbench**. Open a PDF, recognize it page by page, edit the text, and export a searchable PDF.

## Recording

**Capture → Screen record** or **GIF record**: click a window, or drag a region.

- The red frame is drawn outside the selection. Size and aspect can be changed before start. After start the aspect stays locked by default. The control bar can be dragged and collapsed. Before start every setting can be changed. During recording the audio source can be changed, or audio can be turned off, and that takes effect at once.
- **Region recording** writes an MP4. It can record system audio, the microphone, or both, and can overlay the mouse and click highlights (yellow for the left button, blue for the right, green for the middle). The default codec is system H.264, without FFmpeg. x264, x265, and AV1 are also available. AV1 needs an installed FFmpeg. After stop, Explorer selects the file.
- **GIF record** uses the same region capture, then opens a preview. Output frame rate, scale, and palette (32 / 64 / 128 / 256 colors) can be changed. It saves a silent GIF. Capture does not use FFmpeg. A large picture or a long take grows the file quickly. Shrink it or lower the frame rate first.
- **Capture → Record options** also has the frame rate, and the same shorter-side cap as screenshots.

## Speech, translation, dictionary, and face

- **Speech recognition**: Live captions can be offline or streaming. With no saved offline model, it uses Windows speech recognition. Missing languages can be installed under **Help → Install features → Windows OCR/Speech**.
- **Speech synthesis**: Windows speech, SAPI, offline speech, Edge natural voices online, and Google voices in the local Chrome.
- **Voice input**: The hotkey starts it. Press again to finish, and the result is sent to the current input position.
- **Chat**: Uses a configured LLM. WeChat-style bubbles. Clear, microphone input, speak or auto-speak, and it can also open web and local tools. Hidden by default.
- **LLM log**: The main window lists about the latest 1,000 requests. It shows the model, status, tokens, elapsed time, and the request and response. The query string on the address is not saved. Hidden by default.
- **Translation**: Local Opus-MT (Chinese–English), a configured LLM, or translation in the local Chrome. Chrome uses the `chrome.exe` from Settings → General and the same hidden process as speech. The window stays hidden, and Chrome downloads language packs when it needs them, without a click in the page, and the status shows the download progress. A round trip stops at 20 passes, or earlier when the result repeats. **Tools → Translate popup** can choose local ONNX, Chrome, or a configured LLM (the same choice as the main window; it does not pick another model). Both the source and the translation can be spoken.
- **Face**: InsightFace detection and comparison, with optional landmarks and gender and age. Models are downloaded in Install features.
- **Chinese to pinyin**: Without tone marks, or one capital initial per character. Common polyphonic words are in `pinyin.toml` next to the program (for example 重庆 and 银行). The same word in `pinyin.user.toml` overrides it. An empty reading returns to the default reading. An update replaces the system word list and leaves the user list. The web card and `GET/POST /api/pinyin` send only the source text and return both `pinyin` (full spelling) and `py` (initials).
- **Wubi, Japanese, and Korean IME**: Type Wubi, Japanese, or Korean with a candidate bar in this app’s windows and in other programs alike. Install `ime.db` under **Help → Install features**. Turn it on from **Tools**; the tray icon switches language and opens IME settings. It stays off without the lexicon.
- **Simplified/Traditional**: Simplified on top, traditional below. It uses the system API. Traditional follows Taiwan character forms, and it does not replace whole words.
- **Japanese yomi**: Turns a Japanese sentence into readings, by word or by character. Japanese must already be installed on this PC.
- **Calendars**: Gregorian to lunar (sexagenary cycle and leap months), plus Japanese, Taiwan, Hebrew, Hijri, Persian, and other calendars.

### Dictionary

The main window, or **Tools → Dictionary**. Look up Chinese, English, Japanese, and Korean. The release package does not include the dictionary file. **Help → Install features** downloads it next to the program.

- Language buttons: All, Chinese, Japanese, Korean, English.
- Each hit shows the language, the headword, and the gloss, and can be spoken. Expanded, it shows pronunciation, part of speech, senses, phrases, and examples. Examples can be spoken too.
- **Export** writes every hit of this search, not only the rows on screen. txt joins them into one text. csv is a sheet. html and md are a table.
- Double-click, drag, or right-click inside an entry to pop up matching headwords. That popup can choose All, Chinese, Japanese, Korean, or English, apart from the page filter. It lists up to 100 hits, and the rest scroll.
- **Settings → Dictionary** sets how many hits to list at once, how long to keep the database open, the selection popup, and, for each language, the engine, the voice, and the rate. When the engine is Auto, it uses that language’s Windows speech, or Edge when none is installed.
- The dictionary hotkey is empty by default. After it is set: when this is not the main window, it brings the window forward and selects the dictionary (and searches only when the clipboard looks like one word; with nothing to search, the dictionary database stays closed); when it is already the main window, it hides it.
- The web dictionary is its own page at `/dict`. **Dictionary** at the lower left of the tools page, and **Web tools** in the menu and the tray, open it in a new window. It is not embedded in a tool card. Search, the four languages, speech, and export match the window above. The web selection popup stays a short list. **Translate** in that popup opens the translator on the tools page.
- **Selection popup** is on the dictionary page and under **Settings → Dictionary**. It is off by default. While it is on, a drag or double-click in another program shows the same lookup popup as the dictionary, under the selection: a language choice, up to 100 matching headwords that scroll, speak, search, translate, and copy. If the dictionary file is not open yet, that lookup opens it first. It does not take focus. **×** closes the current popup; **Turn off selection popup** disables the switch (same as clearing it on the dictionary page). Another click, Esc, or moving the pointer out of the popup also closes the current popup. The hotkey on the dictionary settings page turns the switch on or off. It is not registered until you set one.

## Files and screencast

### File sync

On the PC, open **File sync**. The phone and the PC are on the same LAN. The release package does not include the APK. **Install on phone** on this page can check and download it, then install from a QR code. On the first connection the phone waits until the PC allows pairing. The question on the PC stays in front.

- The upper list is `sendfile/` next to the program. It shows only that level and does not enter subfolders. Double-click a file to open it with the system. Double-click a folder to open Explorer. List or thumbnails, marquee select, multi-select, Ctrl+X/C/V to cut, copy, and paste, and delete to the Recycle Bin. Cut items fade.
- Dropping files onto the list puts them in the inbox. Dropping onto the lower area sends them to the phone’s paired folder only while the phone is connected. A share or upload from the phone always writes to the PC `sendfile/`. Text has Send, Copy, and Clear.
- **Web manager** uses the same port. The PC opens `/files`. The phone opens `/m`. After login you can upload, create a folder, rename, and delete. A download link does not need a login. The phone page can upload, take a photo, create new items, and send text.

### Screencast

This program receives while it is running. That can be turned off in Settings. **Tray → Tools → Screencast** opens the main window on the screencast page. Use the same Wi-Fi, with guest isolation off.

- **PC to PC**: On the sender, click a saved PC on the left, scan the LAN (this PC is included), or type an IP. The picture is kept within 1000×1000 by default. It only shrinks, and never enlarges. After Start, pick a region first, then choose the audio (speakers, microphone, or both), and only then does it send. The red frame and the toolbar are not part of the picture the other side sees. Pause stops the picture and the audio. The connection stays.
- **Phone to PC**: In the phone app, pick a saved PC or scan the LAN, allow screen capture, then start. A cable can use USB debugging, or a USB accessory. On failure, on disconnect, or when capture is not allowed, the phone says the cast failed.
- The viewer opens in the center of the primary monitor and is not pinned on top. The picture can fit the window or fill it. Closing the window disconnects. Press Tab to show the resolution, frame rate, and the rest. Quality and the audio source can be changed while casting.

## Tools

- **Image convert**: Batch convert to JPG, PNG, palette PNG, or BMP. It can also read GIF and TIFF. A shorter-side cap, rotation, and mirror are available. The list can show thumbnails, and the pane on the right previews the result of those settings. Output goes to `output/` next to each source, to a chosen folder, or replaces the source (it asks before deleting). When OpenCV is already installed, it can also read WebP and more.
- **Batch video convert**: Convert to MP4, MKV, or WebM. By default it can cap the shorter side and keep the audio. The output location matches image convert. It needs FFmpeg.
- **Video to GIF**: Open or drop one video and play it. While a file is dragged over the window, the preview asks you to release. Pick a start and an end, at most about 60 seconds. Frame rate, palette, and the shorter side can be changed. It writes a silent GIF. It needs FFmpeg.
- **Character art**: Plain ASCII. The column count defaults to 250 and the preview font size defaults to 6. With Invert off, darker cells use denser characters, and the threshold leaves cells that are not dark enough blank. A slider sets the preview font size. With Lines on, edges in the picture are drawn with `-`, `|`, `/`, `\`, and `+`. Threshold 0 also keeps faint edges. Full-color character art is drawn in a console window. It can zoom and pan.
- **Pixel art**: Choose a width and a height (1–128), or use 8, 16, 32, or 64 directly. Paint on large cells. A fast stroke still fills every cell it crosses. The preview beside it is the picture at its real pixel size. Copy or save a PNG.
- **Character bitmaps**: Choose a font and a size, and draw each character as a dot matrix. Scan by row or by column, and set the high bit, the low bit, and positive or negative. Export a C array, hex, or a dot-matrix text.
- **QR / barcode**: QR, Data Matrix, Code 128/39, EAN, and UPC. An optional line of the original text can be written under the image. The encoding can be UTF-8, GBK, or hex.
- **Batch rename**: After adding files, edit the target names by hand, or use rules such as `%1` and `#`. Changing the list or the rules recalculates the names. Sort by name or by date.
- **Fast copy**: Copy, cut, delete permanently, delete to the Recycle Bin, or make a symbolic link or a hard link. A same name can overwrite, skip, or rename. After **Register menu**, Explorer has the matching cascade menu. After a cut or a copy, the empty area of a folder can fast-paste.
- **Hash**: MD5, SHA-1, and SHA-256. Paste an expected value to compare.
- **Text tools**: Base64, URL, UTF-8/GBK hex, Unicode escape, case, whitespace, counts, and JSON pretty-print.
- **Markdown reader**: Open or drop a Markdown file. It is turned into HTML and shown with the IE11 engine (WebBrowser) built into Windows 10. Images load from the file’s folder. Web links open in the system browser. The card of the same name under Text on the tools page also previews a dropped file, with the same rules. `POST /api/md` takes `text` or the file as `base64`, an optional `title`, and returns `data.html`.
- **Password generator**: Opening the window generates a batch. Length, character classes, and whether to skip lookalike characters can be changed. It also has word transcription and variants: hand a password sentence to a configured LLM, translate it into other languages, then spell the result with pinyin or roman letters.
- **Network**: The **Network** submenu under Tools has three items: Network, HTTP request debugger, and Ports in use. Network opens a window with Diagnostics, IP scan, and Port scan. Diagnostics covers Ping, DNS, DNS records, Port test, Local network, Wake-on-LAN, ARP lookup, Reverse ARP lookup, WHOIS, Trace route, Ping locate, Proxy locate, IP city, MAC vendor, USB ID, HTTP speed, Location, Network discovery, and Map.
- **Ports in use**: Under Network. It lists TCP and UDP ports that are in use, with listening ports first. Select a row to see the process and its path, and to open that folder.
- **System info**: System, USB, serial ports, printers, sound, fonts, displays, cameras, Bluetooth, network adapters, input devices, physical disks, batteries, PCI and system devices, and problem devices.
- **System tools**: On the Tools menu and the tray System submenu. Read-only lists of processes (name, PID, memory, path, command line), environment variables (user and machine kept apart; semicolons in a value become separate lines in the detail pane), startup items (Run keys, startup folders, logon and boot scheduled tasks), installed programs (name, version, publisher, date, uninstall command), services (name, state, start type, path), installed hotfixes, and the current user's Store and UWP packages. File locks accepts a file or folder by picker or drag-and-drop and lists the processes holding it. File associations takes an extension and shows the default program. Services are listed only.
- **Performance**: On the Tools menu and the tray System submenu. A borderless window you can drag; it remembers where you left it. A slate panel shows two columns of CPU, GPU, and memory use, network upload and download, disk read and write, and CPU or GPU temperature and power when the machine exposes them. A missing sensor is a dash. The pin beside the title keeps the window in front. Right-click turns each field on or off, keeps the window on top, closes it, or opens settings to change the update interval (0.2–5 seconds).
- **File info**: On the Tools menu and the tray File submenu. Pick or drop a file. The header type is compared with the extension. Every file shows size (B/KB/MB plus the exact byte count). For exe/dll, fields close to Explorer **Details** (description, type, file/product version, copyright, size, date modified, language, original filename, and more) plus PE platform (32/64-bit, etc.), whether it is a .NET assembly, CLR version, and target framework. Images show width, height, capture time, and camera maker. GPS is shown only in this window. When ffmpeg is present, duration (to milliseconds), resolution, video/audio codecs, overall and per-stream bit rates, and frame rate are read; without it, WAV and similar files still use header parsing where possible. Otherwise only what the system can read is shown. Text files show whether a BOM is present and whether the bytes look more like UTF-8 or GBK.
- **HTTP request debugger**: Under Network. Requests are saved in groups. Parameters are one `key=value` per line. A POST can be those parameters, JSON, XML, or raw text with a MIME type you type. The list is `httpdbg.json` next to the program. Until there is a sample, it adds a group named 测试, with calls to this program on `127.0.0.1:1224`: status, simplified/traditional, pinyin, USB ID, currency, and Base64 text. After that group is deleted and the list is saved, it is not added again.
- **MIDI synthesizer**: Play with the mouse or the computer keyboard. Sound goes to the selected MIDI output. A MIDI keyboard can be connected. A MIDI file can be opened, and its tracks, tempo, and notes can be changed and saved.
- **Sound recorder**: Pick a microphone and record. System sound that is playing can be included, or recorded on its own. The device list is microphones only; headphones and other playback devices are not listed. After a pause, recording can continue. After stop, play it or save a WAV. An existing WAV can be opened or dropped onto the window.
- **Camera**: Pick a camera and preview it. Preview prefers a mode up to 1280×720 with a higher frame rate. A photo is saved as JPEG or PNG. A video is saved as MP4, and the microphone is optional. Scanning reads a QR code or barcode in the picture and needs the optional OpenCV and ZXing install. The same code is shown again only after at least 3 seconds. The camera under Image on the tools page can take a preview photo or an ImageCapture still, and can zoom, turn on the torch, and focus when the device allows it. When the browser provides it, scanning reads frames with MediaStreamTrackProcessor. The same code again waits 3 seconds and flashes the background. Each accepted code plays a 0.2 second square wave at the same pitch as the beep in the 8-bit synthesizer. It does not record video.
- **Scientific calculator**: The keypad calculator in the Tools menu, the tray, and Math on the tools page. It has trigonometry, logarithms, powers, roots, factorial, percent, and memory. The angle mode switches between degrees and radians. The window has no border. The whole window is the calculator, on a black translucent background.
- **Desktop note**: In the Tools menu and the tray. A borderless translucent window for text, or a to-do list with checkboxes. The color chip at the lower right opens the color picker. The corner mark beside it resizes the window. Close it and open it again, and the text, the list, the color, and the window position are still there.
- **RTSP test server**: In the Tools menu and the tray. It draws a bouncing red ball and a green clock on a dark picture, encodes H.264 with libx264 from `ffmpeg64` at 200 kbps by default, and serves `rtsp://127.0.0.1:<port>/live` on the chosen port (default 8554). Turning **Sound** on adds an 8 kHz PCMU tone. Turn it off before start and that track is absent. Turn it off while running and the tone stops. Several players can watch at once. The RTSP player on the tools page can open this address. With sound checked, it plays PCMU, PCMA, or AAC. After RTSP forward is turned off in Settings, this page no longer accepts that request.
- **Auto key**: Sends keys or clicks in a row to the foreground window. The default F6 starts and stops only while this window is open. The interval can be changed. Nothing is sent while this program is in front. Closing the window stops it.
- **Window manager**: Lists visible top-level windows. While you pick one, a green frame previews it. After it is selected, it can be pinned in front or unpinned.

### Web tools

- **Text**
  - **Text convert**: Encoding, escaping, and common text transforms
  - **Chinese to pinyin**: Characters to pinyin or initials
  - **Markdown reader**: Preview Markdown
  - **Chinese segmentation**: Split by word, sentence, or grapheme
  - **In-browser model**: Run text tasks on the browser’s local model. Speak can filter voices by language. System voices are synthesized by this app and played in the page on the system default output. A link opens the Chrome model page to view and install translation models and offline voice packs
  - **Translate**: Translate text into another language
  - **Password generator**: Generate passwords
  - **Regular expressions**: Find regex matches
  - **Number names**: Write a number in several readings
  - **Test data**: Generate fake data by type
- **Math**
  - **Scientific calculator**: Scientific calculation
  - **Radix conversion**: Convert among base 2, 8, 10, and 16
  - **Unit conversion**: Length, weight, currency, and more
  - **Random number**: A number inside a chosen range
  - **Function graph**: Plot a formula
  - **BMI**: Body mass index from height and weight
  - **Mortgage**: Equal payment or equal principal
  - **Compound interest**: Interest compounded yearly
- **Speech**
  - **Speech**: Read text aloud
  - **Speech recognition**: Turn speech into text
  - **8-bit synthesizer**: Square-wave tones and effects
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
  - **This browser**: What the browser can read about this device
  - **USB IDs**: USB vendor and product
  - **Unicode chart**: Unicode characters
  - **ASCII chart**: ASCII characters
  - **GBK chart**: GBK characters
  - **Morse code**: Morse code
- **Talk**
  - **Chat room**: Send text, pictures, and files

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

## Install, language, and updates

The slim package does not include models or the large runtimes. It includes `7za.dll` and `License.7za.txt`, which extract 7z archives. Screenshot, annotate, long screenshot, Windows OCR, Windows speech, system H.264 recording, most of the tools listed above, file transfer, and the PC side of screencast work after install. Windows OCR, Windows speech recognition, and Windows speech synthesis also need that language already present in the operating system. A missing language can be installed under **Windows OCR/Speech** in the install window.

Barcode, ONNX text recognition, offline speech, face, local Chinese–English translation, the dictionary, the input method, PDF, FFmpeg, CUDA, and DirectML are installed or removed by checking them under **Help → Install features**. On open, installed items are checked. Items about to be added and items about to be removed are shown separately. Checking screenshot OCR or translation also checks shared pieces listed on their own, such as the ONNX CPU runtime. That size is shown on the dependency row and counted once in the total. iGPU DirectML is sized by the NuGet packages to download, about 205 MB. If a package is larger than the estimate, the progress total grows with it. After install those rows stay checked, so they are not marked for removal. **Single components** installs or removes one package at a time, such as FFmpeg, without the other packages a feature would pull in. Using a component that is not installed yet shows a prompt. Speech packs have their own page. The Chrome page can look up, install, and remove translation language packs and offline voice packs for this program’s hidden Chrome. **Chrome models** on that page opens `/chrome` in the local Chrome, where translation models and offline voice packs can be listed, installed, or removed, and filtered by language. Translation install state comes from local files. Voice packs are checked in the Chrome that has this page open. Installed packs are green and missing packs are red. **On-device model** opens `chrome://on-device-internals` in Chrome. Install shows download progress while a pack is downloading. It does not change the user data of everyday browsing. Inference can use the CPU, NVIDIA CUDA, or iGPU DirectML. With no acceleration library installed, it uses the CPU.

**Settings → General** can turn a group of features off. Off hides the matching page and disables its hotkey, its tray entry, and the related API. The UI language is switched on the menu bar or on that same page. Chinese, English, Japanese, Korean, German, Spanish, French, Portuguese, and Russian are included. A missing phrase falls back to Chinese.

**Help → Check for updates** also checks at startup, on an interval. When a newer version exists, it lists the notes for every version after the current one. The notes can be switched between Chinese and English, then you can update or ignore. Put an update file `screenkit_<version>.7z` in the program folder. On startup, or the next time you click Check for updates, it asks whether to install from that file. When GitHub cannot be reached, the check says to put the new 7z in the program folder and click Check for updates again. **Help → Diagnostics** shows whether each item is ready. **Help → Memory** shows usage and can unload models that are already loaded.

## Run

Windows 10/11 (x64). It needs [.NET Framework 4.8](https://dotnet.microsoft.com/download/dotnet-framework/net48).

The local HTTP port defaults to 1224. After **Allow LAN access** is checked, the same LAN can connect. Otherwise it is open only on this PC. File transfer, the web tools, and screencast share this port. `POST /api/ocr` matches Umi-OCR text recognition. `POST /api/qrcode` matches Umi-OCR QR codes: each hit has `format` (`QRCode` for a QR code) and `text`. The command line can recognize images in a batch, list models, or run a few checks.

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
- **SQLite** (`e_sqlite3.dll`) opens the dictionary database. It is in the public domain.

`dict2.db` keeps Chinese, Japanese, Korean, and English in one file. ScreenKit does not build this file. Install features downloads it. Inside it:

- **Chinese.** Characters, words, and idioms come from [mapull/chinese-dictionary](https://github.com/mapull/chinese-dictionary) (a pinyin dictionary). The license is MIT. The upstream notes say the origin of some material is unclear.
- **Japanese.** Word senses come from [JMdict/EDICT](https://www.edrdg.org/wiki/index.php/JMdict-EDICT_Dictionary_Project) (EDRDG). The license is [CC BY-SA 4.0](https://www.edrdg.org/edrdg/license.html). Redistribution has to keep the EDRDG attribution and follow that project’s update terms.
- **Korean.** A derivative of the National Institute of Korean Language dictionary. The license is CC BY-SA 2.0 KR.

Step-by-step use, the API, and the build are in the [full guide](docs/archive/README.md). The phone app is in [android/README.md](android/README.md).
