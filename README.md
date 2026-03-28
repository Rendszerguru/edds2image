# edds2image

**edds2image** is a high-performance tool for converting `.edds` files (Enfusion Engine / Arma Reforger) into standard **PNG**, **TIFF**, and **DDS** formats.

## Key Features
- **Batch Processing:** Automatically converts all files in a folder using multi-threading.
- **Flexible Input:** Supports Drag & Drop, File Association, and Manual Selection.
- **High Performance:** Uses all available CPU cores and in-memory buffering for maximum speed.
- **Portable:** Single executable file, no installation required.

## Usage

### 1. Batch Mode (Default)
Place `edds2image.exe` in the folder with your `.edds` files and run it. It will process everything in that directory.

### 2. Drag and Drop
Drag one or multiple `.edds` files and drop them directly onto `edds2image.exe`.

### 3. File Association
Right-click an `.edds` file -> *Open with* -> *Choose another app* -> Select `edds2image.exe`. 
Once associated, double-clicking any `.edds` file (or pressing Enter) will automatically trigger the conversion for that file and all others in its directory.

### 4. Manual Selection
If you run the app in an empty folder, it will automatically open a **Windows File Dialog**. Select any `.edds` file, and the app will process its entire folder.

## Output
Converted files are neatly organized into subfolders:
- `/png`
- `/tif`
- `/dds`

## Requirements
- Windows 10 / 11 (64-bit)
- .NET 8.0 Desktop Runtime
