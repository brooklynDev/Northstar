SOLUTION := Northstar.sln
PROJECT := Northstar.Desktop/Northstar.Desktop.csproj
APP_NAME := Northstar Explorer
APP_EXECUTABLE := Northstar.Desktop
APP_ICON_FILE := NorthstarApp.icns
DOC_ICON_FILE := NorthstarDocument.icns
CONFIGURATION ?= Release
RUNTIMES := osx-arm64 osx-x64
ARTIFACT_ROOT := artifacts

.PHONY: run build publish clean $(RUNTIMES)

run:
	dotnet run --project $(PROJECT)

build:
	dotnet build $(SOLUTION)

publish: $(RUNTIMES)
	@echo "Created distributable artifacts:"
	@find $(ARTIFACT_ROOT) -maxdepth 2 -name "*.zip" -print

$(RUNTIMES):
	@runtime="$@"; \
	runtime_dir="$(ARTIFACT_ROOT)/$$runtime"; \
	temp_dir="$$(mktemp -d /tmp/northstar-$$runtime-XXXXXX)"; \
	publish_dir="$$temp_dir/publish"; \
	bundle_dir="$$temp_dir/$(APP_NAME).app"; \
	zip_path="$$runtime_dir/$(APP_NAME).zip"; \
	echo "Publishing $$runtime..."; \
	rm -rf "$$runtime_dir"; \
	mkdir -p "$$runtime_dir"; \
	rm -rf "$(ARTIFACT_ROOT)/packages" "$(ARTIFACT_ROOT)/publish"; \
	dotnet publish $(PROJECT) -c $(CONFIGURATION) -r $$runtime --self-contained true -o "$$publish_dir"; \
	mkdir -p "$$bundle_dir/Contents/MacOS" "$$bundle_dir/Contents/Resources"; \
	cp -R "$$publish_dir/." "$$bundle_dir/Contents/MacOS/"; \
	cp "Northstar.Desktop/Assets/$(APP_ICON_FILE)" "$$bundle_dir/Contents/Resources/$(APP_ICON_FILE)"; \
	cp "Northstar.Desktop/Assets/$(DOC_ICON_FILE)" "$$bundle_dir/Contents/Resources/$(DOC_ICON_FILE)"; \
	chmod +x "$$bundle_dir/Contents/MacOS/$(APP_EXECUTABLE)"; \
	printf '%s\n' \
	'<?xml version="1.0" encoding="UTF-8"?>' \
	'<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">' \
	'<plist version="1.0">' \
	'<dict>' \
	'  <key>CFBundleName</key>' \
	'  <string>$(APP_NAME)</string>' \
	'  <key>CFBundleDisplayName</key>' \
	'  <string>$(APP_NAME)</string>' \
	'  <key>CFBundleIdentifier</key>' \
	'  <string>com.northstar.explorer</string>' \
	'  <key>CFBundleVersion</key>' \
	'  <string>1.0.0</string>' \
	'  <key>CFBundleShortVersionString</key>' \
	'  <string>1.0.0</string>' \
	'  <key>CFBundlePackageType</key>' \
	'  <string>APPL</string>' \
	'  <key>CFBundleIconFile</key>' \
	'  <string>$(APP_ICON_FILE)</string>' \
	'  <key>CFBundleExecutable</key>' \
	'  <string>$(APP_EXECUTABLE)</string>' \
	'  <key>CFBundleDocumentTypes</key>' \
	'  <array>' \
	'    <dict>' \
	'      <key>CFBundleTypeName</key>' \
	'      <string>Northstar Document</string>' \
	'      <key>CFBundleTypeRole</key>' \
	'      <string>Editor</string>' \
	'      <key>CFBundleTypeExtensions</key>' \
	'      <array>' \
	'        <string>northstar</string>' \
	'      </array>' \
	'      <key>CFBundleTypeIconFile</key>' \
	'      <string>$(DOC_ICON_FILE)</string>' \
	'    </dict>' \
	'  </array>' \
	'  <key>LSMinimumSystemVersion</key>' \
	'  <string>12.0</string>' \
	'</dict>' \
	'</plist>' > "$$bundle_dir/Contents/Info.plist"; \
	rm -f "$$zip_path"; \
	ditto -c -k --sequesterRsrc --keepParent "$$bundle_dir" "$$zip_path"; \
	rm -rf "$$temp_dir"; \
	echo "Packaged $$zip_path"

clean:
	rm -rf $(ARTIFACT_ROOT)
