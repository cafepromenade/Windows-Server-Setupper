'use strict';

const fs = require('node:fs');
const path = require('node:path');
const { rcedit } = require('rcedit');

module.exports = async function applyAppIcon(context) {
  if (context.electronPlatformName !== 'win32') return;
  const executable = path.join(context.appOutDir, `${context.packager.appInfo.productFilename}.exe`);
  const icon = path.join(context.packager.projectDir, 'assets', 'app.ico');
  if (!fs.existsSync(executable)) throw new Error(`Packaged executable is missing: ${executable}`);
  if (!fs.existsSync(icon)) throw new Error(`Application icon is missing: ${icon}`);
  await rcedit(executable, {
    icon,
    'file-version': context.packager.appInfo.version,
    'product-version': context.packager.appInfo.version,
    'version-string': {
      CompanyName: 'Windows Server Tools contributors',
      FileDescription: 'Guided Microsoft Exchange Server installer',
      InternalName: 'ExchangeAutoInstaller',
      LegalCopyright: 'Copyright Windows Server Tools contributors',
      OriginalFilename: 'Exchange Auto Installer.exe',
      ProductName: 'Exchange Auto Installer'
    }
  });
  console.log(`Applied the committed multi-resolution icon to ${executable}.`);
};
