SOLUTION := Northstar.sln
PROJECT := Northstar.Desktop/Northstar.Desktop.csproj
APP_NAME := Northstar Explorer
APP_EXECUTABLE := Northstar.Desktop
CONFIGURATION ?= Release
RUNTIMES := osx-arm64 osx-x64
ARTIFACT_ROOT := artifacts
PUBLISH_ROOT := $(ARTIFACT_ROOT)/publish
PACKAGE_ROOT := $(ARTIFACT_ROOT)/packages

.PHONY: run build publish clean $(RUNTIMES)

run:
	dotnet run --project $(PROJECT)

build:
	dotnet build $(SOLUTION)

publish: $(RUNTIMES)
	@echo "Created distributable app bundles in $(PACKAGE_ROOT):"
	@ls -1 $(PACKAGE_ROOT)

$(RUNTIMES):
	@runtime="$@"; \
	publish_dir="$(PUBLISH_ROOT)/$$runtime"; \
	bundle_name="$(APP_NAME)-$$runtime.app"; \
	bundle_dir="$(PACKAGE_ROOT)/$$bundle_name"; \
	zip_path="$(PACKAGE_ROOT)/$(APP_NAME)-$$runtime.zip"; \
	echo "Publishing $$runtime..."; \
	dotnet publish $(PROJECT) -c $(CONFIGURATION) -r $$runtime --self-contained true -o "$$publish_dir"; \
	rm -rf "$$bundle_dir"; \
	mkdir -p "$$bundle_dir/Contents/MacOS" "$$bundle_dir/Contents/Resources"; \
	cp -R "$$publish_dir/." "$$bundle_dir/Contents/MacOS/"; \
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
	'  <key>CFBundleExecutable</key>' \
	'  <string>$(APP_EXECUTABLE)</string>' \
	'  <key>LSMinimumSystemVersion</key>' \
	'  <string>12.0</string>' \
	'</dict>' \
	'</plist>' > "$$bundle_dir/Contents/Info.plist"; \
	rm -f "$$zip_path"; \
	ditto -c -k --sequesterRsrc --keepParent "$$bundle_dir" "$$zip_path"; \
	echo "Packaged $$zip_path"

clean:
	rm -rf $(ARTIFACT_ROOT)
