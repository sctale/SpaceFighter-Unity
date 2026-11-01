// 版本号单一来源：package.json -> 菜单脚注 + Android versionName/versionCode
const fs = require('fs');
const path = require('path');

const pkg = JSON.parse(fs.readFileSync(path.join(__dirname, '..', 'package.json'), 'utf8'));
const parts = pkg.version.split('.').map(Number);
if (parts.length !== 3 || parts.some((n) => !Number.isInteger(n) || n < 0) || parts[2] > 9) {
  console.error('sync-version: version must be x.y.z with a single-digit patch, got ' + pkg.version);
  process.exit(1);
}
const versionCode = parts[0] * 10000 + parts[1] * 100 + parts[2] * 10;

const htmlFile = path.join(__dirname, '..', 'www', 'index.html');
let html = fs.readFileSync(htmlFile, 'utf8');
const re = /(<p class="menu-foot">)v[0-9]+(?:\.[0-9]+){1,2}( · .+?<\/p>)/;
if (!re.test(html)) {
  console.error('sync-version: menu-foot marker not found in www/index.html');
  process.exit(1);
}
const nextHtml = html.replace(re, `$1v${pkg.version}$2`);
if (nextHtml !== html) {
  fs.writeFileSync(htmlFile, nextHtml);
  console.log(`sync-version: menu foot -> v${pkg.version}`);
}

const gradleFile = path.join(__dirname, '..', 'android', 'app', 'build.gradle');
let gradle = fs.readFileSync(gradleFile, 'utf8');
const nextGradle = gradle
  .replace(/versionCode\s+\d+/, `versionCode ${versionCode}`)
  .replace(/versionName\s+"[^"]+"/, `versionName "${pkg.version}"`);
if (nextGradle !== gradle) {
  fs.writeFileSync(gradleFile, nextGradle);
  console.log(`sync-version: android -> ${pkg.version} (${versionCode})`);
}
