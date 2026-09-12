// Post-build MSI fixups for the Visual Studio Installer project.
// Uses only the Windows Installer automation API built into Windows.

if (WScript.Arguments.length < 1 || WScript.Arguments.length > 3) {
    WScript.Echo("Usage: cscript //nologo FixMsiDirectories.js <path-to-msi> [banner-bmp] [icon-ico]");
    WScript.Quit(2);
}

var msiPath = WScript.Arguments.Item(0);
var bannerPath = WScript.Arguments.length >= 2 ? WScript.Arguments.Item(1) : "";
var iconPath = WScript.Arguments.length >= 3 ? WScript.Arguments.Item(2) : "";
var fso = new ActiveXObject("Scripting.FileSystemObject");

if (!fso.FileExists(msiPath)) {
    WScript.Echo("MSI not found: " + msiPath);
    WScript.Quit(3);
}

var installer = new ActiveXObject("WindowsInstaller.Installer");
var database = installer.OpenDatabase(msiPath, 1); // msiOpenDatabaseModeTransact

function execute(sql) {
    var view = database.OpenView(sql);
    view.Execute();
    view.Close();
}

function executeWithStrings(sql, values) {
    var view = database.OpenView(sql);
    var record = installer.CreateRecord(values.length);
    for (var i = 0; i < values.length; i++) {
        record.StringData(i + 1) = values[i];
    }
    view.Execute(record);
    view.Close();
}

function fetchDirectories() {
    var rows = [];
    var view = database.OpenView("SELECT `Directory`, `Directory_Parent`, `DefaultDir` FROM `Directory`");
    view.Execute();
    var record;
    while ((record = view.Fetch()) !== null) {
        rows.push({
            id: record.StringData(1),
            parent: record.StringData(2),
            defaultDir: record.StringData(3)
        });
    }
    view.Close();
    return rows;
}

function getLongPath(defaultDir) {
    var value = defaultDir || "";
    var pipe = value.indexOf("|");
    if (pipe >= 0) {
        value = value.substring(pipe + 1);
    }
    return value.replace(/^[\\\/]+|[\\\/]+$/g, "");
}

function safeKeyPart(value) {
    return value.replace(/[^A-Za-z0-9_]/g, "").substr(0, 24);
}

function fixNestedDirectories() {
    var rows = fetchDirectories();
    var fixedCount = 0;
    var generated = 0;

    for (var r = 0; r < rows.length; r++) {
        var row = rows[r];
        var path = getLongPath(row.defaultDir);

        if (!/[\\\/]/.test(path)) {
            continue;
        }

        var rawParts = path.split(/[\\\/]+/);
        var parts = [];
        for (var p = 0; p < rawParts.length; p++) {
            if (rawParts[p].length > 0) {
                parts.push(rawParts[p]);
            }
        }

        if (parts.length < 2) {
            continue;
        }

        var parent = row.parent;
        for (var i = 0; i < parts.length - 1; i++) {
            generated++;
            var newId = "_LUMENFIX_" + generated + "_" + safeKeyPart(row.id);
            executeWithStrings(
                "INSERT INTO `Directory` (`Directory`, `Directory_Parent`, `DefaultDir`) VALUES (?, ?, ?)",
                [newId, parent, parts[i]]
            );
            parent = newId;
        }

        executeWithStrings(
            "UPDATE `Directory` SET `Directory_Parent`=?, `DefaultDir`=? WHERE `Directory`=?",
            [parent, parts[parts.length - 1], row.id]
        );

        fixedCount++;
        WScript.Echo("Fixed MSI directory: " + row.defaultDir + " -> " + path);
    }

    WScript.Echo("Nested directory validation complete. Repaired entries: " + fixedCount);
}

function ensureProperty(name, value) {
    var view = database.OpenView("SELECT `Value` FROM `Property` WHERE `Property`=?");
    var search = installer.CreateRecord(1);
    search.StringData(1) = name;
    view.Execute(search);
    var found = view.Fetch();
    view.Close();

    if (found !== null) {
        executeWithStrings("UPDATE `Property` SET `Value`=? WHERE `Property`=?", [value, name]);
    } else {
        executeWithStrings("INSERT INTO `Property` (`Property`, `Value`) VALUES (?, ?)", [name, value]);
    }
}

function getLongName(value) {
    var text = value || "";
    var pipe = text.indexOf("|");
    return pipe >= 0 ? text.substring(pipe + 1) : text;
}

function ensureDirectory(id, parent, defaultDir) {
    var view = database.OpenView("SELECT `Directory` FROM `Directory` WHERE `Directory`=?");
    var q = installer.CreateRecord(1);
    q.StringData(1) = id;
    view.Execute(q);
    var found = view.Fetch();
    view.Close();
    if (found === null) {
        executeWithStrings(
            "INSERT INTO `Directory` (`Directory`, `Directory_Parent`, `DefaultDir`) VALUES (?, ?, ?)",
            [id, parent, defaultDir]
        );
    }
}

function findLumenExe() {
    var view = database.OpenView("SELECT `File`, `Component_`, `FileName` FROM `File`");
    view.Execute();
    var row;
    var result = null;
    while ((row = view.Fetch()) !== null) {
        if (getLongName(row.StringData(3)).toLowerCase() === "lumen.exe") {
            result = {
                file: row.StringData(1),
                component: row.StringData(2),
                fileName: row.StringData(3)
            };
            break;
        }
    }
    view.Close();
    if (result === null) {
        throw new Error("Lumen.exe was not found in the MSI File table.");
    }

    var componentView = database.OpenView("SELECT `Directory_` FROM `Component` WHERE `Component`=?");
    var componentQuery = installer.CreateRecord(1);
    componentQuery.StringData(1) = result.component;
    componentView.Execute(componentQuery);
    var componentRow = componentView.Fetch();
    componentView.Close();
    if (componentRow === null) {
        throw new Error("The component containing Lumen.exe was not found.");
    }
    result.directory = componentRow.StringData(1);

    var featureView = database.OpenView("SELECT `Feature_` FROM `FeatureComponents` WHERE `Component_`=?");
    var featureQuery = installer.CreateRecord(1);
    featureQuery.StringData(1) = result.component;
    featureView.Execute(featureQuery);
    var featureRow = featureView.Fetch();
    featureView.Close();
    if (featureRow === null) {
        throw new Error("The feature containing Lumen.exe was not found.");
    }
    result.feature = featureRow.StringData(1);
    return result;
}

function removeGeneratedLumenShortcuts() {
    var rows = [];
    var view = database.OpenView("SELECT `Shortcut`, `Name` FROM `Shortcut`");
    view.Execute();
    var row;
    while ((row = view.Fetch()) !== null) {
        if (getLongName(row.StringData(2)).toLowerCase() === "lumen") {
            rows.push(row.StringData(1));
        }
    }
    view.Close();

    for (var i = 0; i < rows.length; i++) {
        executeWithStrings("DELETE FROM `Shortcut` WHERE `Shortcut`=?", [rows[i]]);
        WScript.Echo("Removed generated shortcut row: " + rows[i]);
    }
}

function deleteKnownShortcutComponent(component, registryId) {
    try { executeWithStrings("DELETE FROM `Shortcut` WHERE `Component_`=?", [component]); } catch (e1) { }
    try { executeWithStrings("DELETE FROM `Registry` WHERE `Registry`=?", [registryId]); } catch (e2) { }
    try { executeWithStrings("DELETE FROM `FeatureComponents` WHERE `Component_`=?", [component]); } catch (e3) { }
    try { executeWithStrings("DELETE FROM `Component` WHERE `Component`=?", [component]); } catch (e4) { }
}

function addShortcutComponent(feature, component, componentGuid, appDirectory, condition, registryId, registryName) {
    executeWithStrings(
        "INSERT INTO `Component` (`Component`, `ComponentId`, `Directory_`, `Attributes`, `Condition`, `KeyPath`) VALUES (?, ?, ?, 4, ?, ?)",
        [component, componentGuid, appDirectory, condition, registryId]
    );
    executeWithStrings(
        "INSERT INTO `FeatureComponents` (`Feature_`, `Component_`) VALUES (?, ?)",
        [feature, component]
    );
    executeWithStrings(
        "INSERT INTO `Registry` (`Registry`, `Root`, `Key`, `Name`, `Value`, `Component_`) VALUES (?, 2, ?, ?, ?, ?)",
        [registryId, "Software\\Lumen\\Installer", registryName, "1", component]
    );
}

function addShortcut(shortcut, directory, component, target, workingDirectory) {
    executeWithStrings(
        "INSERT INTO `Shortcut` (`Shortcut`, `Directory_`, `Name`, `Component_`, `Target`, `Description`, `ShowCmd`, `WkDir`) VALUES (?, ?, ?, ?, ?, ?, 1, ?)",
        [shortcut, directory, "Lumen", component, target, "Launch Lumen", workingDirectory]
    );
}

function addSecureProperty(name) {
    var view = database.OpenView("SELECT `Value` FROM `Property` WHERE `Property`='SecureCustomProperties'");
    view.Execute();
    var row = view.Fetch();
    view.Close();
    var value = row !== null ? row.StringData(1) : "";
    var parts = value ? value.split(";") : [];
    var exists = false;
    for (var i = 0; i < parts.length; i++) {
        if (parts[i].toUpperCase() === name.toUpperCase()) {
            exists = true;
            break;
        }
    }
    if (!exists) {
        parts.push(name);
    }
    ensureProperty("SecureCustomProperties", parts.join(";"));
}

function configureShortcuts() {
    var lumenExe = findLumenExe();

    ensureDirectory("ProgramMenuFolder", "TARGETDIR", ".");
    ensureDirectory("LumenProgramsFolder", "ProgramMenuFolder", "Lumen");
    ensureDirectory("DesktopFolder", "TARGETDIR", ".");

    removeGeneratedLumenShortcuts();
    deleteKnownShortcutComponent("LumenStartMenuShortcut", "LumenStartMenuShortcutReg");
    deleteKnownShortcutComponent("LumenDesktopShortcut", "LumenDesktopShortcutReg");

    addSecureProperty("STARTMENUSHORTCUT");
    addSecureProperty("DESKTOPSHORTCUT");
    ensureProperty("STARTMENUSHORTCUT", "1");
    ensureProperty("DESKTOPSHORTCUT", "1");

    addShortcutComponent(
        lumenExe.feature,
        "LumenStartMenuShortcut",
        "{1B4BB823-322B-53E8-9882-7B2B19D232A4}",
        lumenExe.directory,
        "STARTMENUSHORTCUT=1",
        "LumenStartMenuShortcutReg",
        "StartMenuShortcut"
    );
    addShortcutComponent(
        lumenExe.feature,
        "LumenDesktopShortcut",
        "{425ED5CB-988A-523D-AD27-1AFD9A55D578}",
        lumenExe.directory,
        "DESKTOPSHORTCUT=1",
        "LumenDesktopShortcutReg",
        "DesktopShortcut"
    );

    // Avoid [#FileKey] here. Publish Items can rewrite file identifiers in a way
    // that makes the formatted reference fail later with Windows Installer 2715.
    // TARGETDIR is Lumen's application folder in this setup project.
    var target = "[TARGETDIR]Lumen.exe";
    addShortcut("LumenStartMenuShortcutRow", "LumenProgramsFolder", "LumenStartMenuShortcut", target, lumenExe.directory);
    addShortcut("LumenDesktopShortcutRow", "DesktopFolder", "LumenDesktopShortcut", target, lumenExe.directory);

    WScript.Echo("Shortcut feature: " + lumenExe.feature);
    WScript.Echo("Created conditional Start Menu/Desktop shortcuts targeting [TARGETDIR]Lumen.exe; Windows will use the icon embedded in Lumen.exe.");
}

function insertIconStream(name, sourcePath) {
    try {
        executeWithStrings("DELETE FROM `Icon` WHERE `Name`=?", [name]);
    } catch (e) {
    }

    var view = database.OpenView("INSERT INTO `Icon` (`Name`, `Data`) VALUES (?, ?)");
    var record = installer.CreateRecord(2);
    record.StringData(1) = name;
    record.SetStream(2, sourcePath);
    view.Execute(record);
    view.Close();
}

function applyProductIcon() {
    if (!iconPath || !fso.FileExists(iconPath)) {
        WScript.Echo("Lumen ICO was not supplied; keeping generated Add/Remove Programs icon settings.");
        return;
    }

    insertIconStream("LumenProduct.ico", iconPath);
    ensureProperty("ARPPRODUCTICON", "LumenProduct.ico");
    WScript.Echo("Applied Lumen.ico to Add/Remove Programs.");
}

function applyBanner() {
    if (!bannerPath || !fso.FileExists(bannerPath)) {
        WScript.Echo("Lumen banner was not supplied; keeping default installer artwork.");
        return;
    }

    try {
        execute("DELETE FROM `Binary` WHERE `Name`='LumenBanner'");
    } catch (e) {
        // Fresh packages normally have no LumenBanner row.
    }

    var insertView = database.OpenView("INSERT INTO `Binary` (`Name`, `Data`) VALUES (?, ?)");
    var record = installer.CreateRecord(2);
    record.StringData(1) = "LumenBanner";
    record.SetStream(2, bannerPath);
    insertView.Execute(record);
    insertView.Close();

    execute("UPDATE `Control` SET `Text`='LumenBanner' WHERE `Control`='BannerBmp'");
    WScript.Echo("Applied Lumen branding banner to installer dialogs.");
}

try {
    fixNestedDirectories();
    configureShortcuts();
    applyProductIcon();
    applyBanner();
    database.Commit();
    WScript.Echo("Lumen MSI post-build polish complete.");
} catch (e) {
    WScript.StdErr.WriteLine("Lumen MSI post-build failed: " + e.message);
    WScript.Quit(10);
}
