import json
import os
import subprocess
import threading
import tkinter as tk
from tkinter import filedialog, messagebox, scrolledtext, ttk

BASE = os.path.dirname(os.path.abspath(__file__))
CONFIG_PATH = os.path.join(BASE, "build-tool.config.json")
SCRIPT_PATH = os.path.join(BASE, "build-release.ps1")
RELEASE_DIR = os.path.join(BASE, "release")

VIDEO_EXTENSIONS = {".mp4", ".m4v", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".mpg", ".mpeg"}
EDITIONS = ("Consumer", "Normal", "Developer")

FEATURE_LABELS = (
    ("adsTab", "Ads tab (playlist editor, add and remove ads)"),
    ("adsSettings", "Ad settings (enable, frequency, skip delay)"),
    ("advanced", "Advanced page (diagnostics, log, engine tools)"),
    ("allAdFolders", "Scan all ad folders (Videos, app data, app folder)"),
)


def feature_defaults(edition):
    is_dev = edition == "Developer"
    is_consumer = edition == "Consumer"
    return {
        "adsTab": not is_consumer,
        "adsSettings": not is_consumer,
        "advanced": is_dev,
        "allAdFolders": not is_consumer,
    }


class BuildTool:
    def __init__(self, root):
        self.root = root
        self.root.title("Tomato Flow Build Tool")
        self.root.minsize(860, 680)
        self.root.geometry("900x720")

        self.ads = []
        self.loading = False

        self.edition = tk.StringVar(value="Consumer")
        self.version = tk.StringVar(value="1.1.0")
        self.flags = {key: tk.BooleanVar(value=False) for key, _ in FEATURE_LABELS}
        self.status = tk.StringVar(value="Ready.")

        self.build_ui()
        self.load_config()

        self.edition.trace_add("write", self.on_edition_change)
        self.root.protocol("WM_DELETE_WINDOW", self.on_close)

    def build_ui(self):
        style = ttk.Style()
        if "vista" in style.theme_names():
            style.theme_use("vista")

        outer = ttk.Frame(self.root, padding=14)
        outer.pack(fill="both", expand=True)

        header = ttk.Frame(outer)
        header.pack(fill="x")
        ttk.Label(header, text="Tomato Flow Build Tool", font=("Segoe UI", 16, "bold")).pack(side="left")
        ttk.Label(header, text="Configure an edition, choose ads, then build.",
                  foreground="#666666").pack(side="left", padx=(12, 0), pady=(6, 0))

        top = ttk.Frame(outer)
        top.pack(fill="x", pady=(12, 0))

        build_box = ttk.LabelFrame(top, text="Build", padding=12)
        build_box.pack(side="left", fill="both", expand=True)

        ttk.Label(build_box, text="Edition").grid(row=0, column=0, sticky="w", pady=4)
        edition_box = ttk.Combobox(build_box, textvariable=self.edition, values=EDITIONS,
                                   state="readonly", width=18)
        edition_box.grid(row=0, column=1, sticky="w", padx=(10, 0), pady=4)

        ttk.Label(build_box, text="Version").grid(row=1, column=0, sticky="w", pady=4)
        ttk.Entry(build_box, textvariable=self.version, width=20).grid(
            row=1, column=1, sticky="w", padx=(10, 0), pady=4)

        ttk.Label(
            build_box,
            text="Consumer has no Ads tab or advanced tools.\n"
                 "Normal adds the Ads tab and ad settings.\n"
                 "Developer adds the Advanced page and diagnostics.",
            foreground="#666666",
            justify="left",
        ).grid(row=2, column=0, columnspan=2, sticky="w", pady=(10, 0))

        features_box = ttk.LabelFrame(top, text="Features", padding=12)
        features_box.pack(side="left", fill="both", expand=True, padx=(12, 0))

        for row, (key, label) in enumerate(FEATURE_LABELS):
            ttk.Checkbutton(features_box, text=label, variable=self.flags[key]).grid(
                row=row, column=0, sticky="w", pady=3)

        ads_box = ttk.LabelFrame(outer, text="Bundled ads (leave empty to ship none)", padding=12)
        ads_box.pack(fill="both", expand=True, pady=(12, 0))

        list_frame = ttk.Frame(ads_box)
        list_frame.pack(side="left", fill="both", expand=True)

        self.ads_list = tk.Listbox(list_frame, selectmode="extended", height=8,
                                   font=("Consolas", 9), activestyle="none")
        scroll = ttk.Scrollbar(list_frame, orient="vertical", command=self.ads_list.yview)
        self.ads_list.configure(yscrollcommand=scroll.set)
        self.ads_list.pack(side="left", fill="both", expand=True)
        scroll.pack(side="right", fill="y")

        buttons = ttk.Frame(ads_box)
        buttons.pack(side="left", fill="y", padx=(12, 0))
        ttk.Button(buttons, text="Add files...", command=self.add_files, width=20).pack(fill="x", pady=2)
        ttk.Button(buttons, text="Add folder...", command=self.add_folder, width=20).pack(fill="x", pady=2)
        ttk.Button(buttons, text="Remove selected", command=self.remove_selected, width=20).pack(fill="x", pady=2)
        ttk.Button(buttons, text="Clear", command=self.clear_ads, width=20).pack(fill="x", pady=2)

        actions = ttk.Frame(outer)
        actions.pack(fill="x", pady=(12, 0))

        self.build_button = ttk.Button(actions, text="Build edition", command=self.start_build)
        self.build_button.pack(side="left")
        ttk.Button(actions, text="Open release folder", command=self.open_release).pack(side="left", padx=(8, 0))
        ttk.Button(actions, text="Save config", command=self.save_config).pack(side="left", padx=(8, 0))

        ttk.Label(actions, textvariable=self.status, foreground="#555555").pack(side="right")

        output_box = ttk.LabelFrame(outer, text="Build output", padding=8)
        output_box.pack(fill="both", expand=True, pady=(12, 0))

        self.output = scrolledtext.ScrolledText(output_box, height=18, font=("Consolas", 9), wrap="word")
        self.output.pack(fill="both", expand=True)
        self.output.configure(state="disabled")

    def on_edition_change(self, *_):
        if self.loading:
            return
        defaults = feature_defaults(self.edition.get())
        for key, var in self.flags.items():
            var.set(defaults[key])

    def add_files(self):
        paths = filedialog.askopenfilenames(
            title="Choose ad videos",
            filetypes=[
                ("Video files", "*.mp4 *.m4v *.mkv *.avi *.mov *.wmv *.webm *.mpg *.mpeg"),
                ("All files", "*.*"),
            ],
        )
        self.add_paths(paths)

    def add_folder(self):
        folder = filedialog.askdirectory(title="Choose a folder of ad videos")
        if not folder:
            return
        found = []
        for current, _, files in os.walk(folder):
            for name in files:
                if os.path.splitext(name)[1].lower() in VIDEO_EXTENSIONS:
                    found.append(os.path.join(current, name))
        self.add_paths(found)

    def add_paths(self, paths):
        added = 0
        existing = {os.path.normcase(path) for path in self.ads}
        for path in paths:
            if not path or os.path.splitext(path)[1].lower() not in VIDEO_EXTENSIONS:
                continue
            normalized = os.path.normcase(path)
            if normalized in existing:
                continue
            self.ads.append(path)
            existing.add(normalized)
            added += 1
        self.refresh_ads_list()
        self.status.set(f"{added} video(s) added." if added else "No new videos added.")

    def remove_selected(self):
        indexes = list(self.ads_list.curselection())
        for index in reversed(indexes):
            del self.ads[index]
        self.refresh_ads_list()
        self.status.set(f"{len(indexes)} video(s) removed.")

    def clear_ads(self):
        self.ads.clear()
        self.refresh_ads_list()
        self.status.set("Bundled ads cleared.")

    def refresh_ads_list(self):
        self.ads_list.delete(0, tk.END)
        for path in self.ads:
            self.ads_list.insert(tk.END, f"{os.path.basename(path)}    ({os.path.dirname(path)})")

    def current_config(self):
        return {
            "edition": self.edition.get(),
            "version": self.version.get().strip() or "1.1.0",
            "features": {key: bool(var.get()) for key, var in self.flags.items()},
            "ads": list(self.ads),
        }

    def save_config(self, quiet=False):
        try:
            with open(CONFIG_PATH, "w", encoding="utf-8") as handle:
                json.dump(self.current_config(), handle, indent=2)
            if not quiet:
                self.status.set("Config saved.")
        except OSError as error:
            messagebox.showerror("Could not save config", str(error))

    def load_config(self):
        if not os.path.exists(CONFIG_PATH):
            self.apply_defaults()
            return
        try:
            with open(CONFIG_PATH, "r", encoding="utf-8") as handle:
                data = json.load(handle)
        except (OSError, ValueError):
            self.apply_defaults()
            return

        self.loading = True
        edition = data.get("edition", "Consumer")
        if edition in EDITIONS:
            self.edition.set(edition)
        self.version.set(str(data.get("version", "1.1.0")))
        features = data.get("features") or {}
        defaults = feature_defaults(self.edition.get())
        for key, var in self.flags.items():
            value = features.get(key, defaults[key])
            var.set(bool(value))
        self.ads = [path for path in data.get("ads", []) if isinstance(path, str)]
        self.loading = False
        self.refresh_ads_list()
        self.status.set("Loaded build-tool.config.json.")

    def apply_defaults(self):
        defaults = feature_defaults(self.edition.get())
        for key, var in self.flags.items():
            var.set(defaults[key])

    def start_build(self):
        if not os.path.exists(SCRIPT_PATH):
            messagebox.showerror("Missing script", f"Could not find {SCRIPT_PATH}")
            return

        self.save_config(quiet=True)
        command = [
            "powershell",
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            SCRIPT_PATH,
            "-Config",
            CONFIG_PATH,
        ]

        self.build_button.configure(state="disabled")
        self.set_output("")
        self.status.set(f"Building {self.edition.get()} {self.version.get()}...")

        thread = threading.Thread(target=self.run_build, args=(command,), daemon=True)
        thread.start()

    def run_build(self, command):
        creation_flags = getattr(subprocess, "CREATE_NO_WINDOW", 0) if os.name == "nt" else 0
        try:
            process = subprocess.Popen(
                command,
                cwd=BASE,
                stdout=subprocess.PIPE,
                stderr=subprocess.STDOUT,
                text=True,
                encoding="utf-8",
                errors="replace",
                creationflags=creation_flags,
            )
        except OSError as error:
            self.root.after(0, self.finish_build, 1, str(error))
            return

        if process.stdout is not None:
            for line in process.stdout:
                self.root.after(0, self.append_output, line)
        code = process.wait()
        self.root.after(0, self.finish_build, code, None)

    def finish_build(self, code, error):
        self.build_button.configure(state="normal")
        if error:
            self.append_output(f"\nCould not start the build: {error}\n")
            self.status.set("Build failed to start.")
            messagebox.showerror("Build failed", error)
            return

        if code == 0:
            self.append_output("\nBuild finished. The zip is in the release folder.\n")
            self.status.set("Build finished.")
            messagebox.showinfo("Build finished", "The zip is ready in the release folder.")
        else:
            self.append_output(f"\nBuild failed with exit code {code}.\n")
            self.status.set(f"Build failed ({code}).")
            messagebox.showerror("Build failed", f"Exit code {code}. Check the build output.")

    def set_output(self, text):
        self.output.configure(state="normal")
        self.output.delete("1.0", tk.END)
        self.output.insert(tk.END, text)
        self.output.configure(state="disabled")

    def append_output(self, text):
        self.output.configure(state="normal")
        self.output.insert(tk.END, text)
        self.output.see(tk.END)
        self.output.configure(state="disabled")

    def open_release(self):
        if not os.path.isdir(RELEASE_DIR):
            messagebox.showinfo("Nothing yet", "Build an edition first.")
            return
        os.startfile(RELEASE_DIR)

    def on_close(self):
        self.save_config(quiet=True)
        self.root.destroy()


def main():
    root = tk.Tk()
    BuildTool(root)
    root.mainloop()


if __name__ == "__main__":
    main()
