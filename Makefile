.PHONY: build app universal run dump test clean

# Debug build of everything (quick compile check)
build:
	swift build

# Release .app for this Mac -> dist/Kavosh Diagnostics.app
app:
	./Scripts/build-app.sh

# Release .app for Apple silicon + Intel (needs full Xcode)
universal:
	./Scripts/build-app.sh --universal

# Build and open the app
run:
	./Scripts/build-app.sh --open

# Print everything the app can read about this Mac, without opening a window
dump:
	swift run -c release KavoshDiagnostics --dump

test:
	swift test

clean:
	rm -rf .build dist
