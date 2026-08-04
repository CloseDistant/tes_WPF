const crypto = require('crypto');
const fs = require('fs');
const path = require('path');
const zlib = require('zlib');

const projectRoot = path.join(__dirname, '..');
const viewer = fs.readFileSync(
  path.join(projectRoot, 'Assets', 'FemViewer', 'fem-result-viewer.html'),
  'utf8');
const viewModel = fs.readFileSync(
  path.join(projectRoot, 'ViewModels', 'FemSimulationViewModel.cs'),
  'utf8');
const viewCode = fs.readFileSync(
  path.join(projectRoot, 'Views', 'FemSimulationView.xaml.cs'),
  'utf8');
const packageLoader = fs.readFileSync(
  path.join(projectRoot, 'Services', 'Fem', 'FemResultPackage.cs'),
  'utf8');

const moduleMatch = viewer.match(/<script type="module">([\s\S]*?)<\/script>/);
if (!moduleMatch) throw new Error('Dynamic result viewer module is missing.');
const checkable = moduleMatch[1].replace(/^import .*$/gm, '');
new Function(`return (async()=>{${checkable}});`);

for (const token of [
  'await fetch(dataUrl',
  'resultPayload.structures',
  'valuesTiEnvelopeVm',
  "postMessage('ready')",
  'const atlasMeshes=',
  'brainDisplaySource.scalarValuesVm',
  'function applyTargetSurfaceColors(mesh,mode)',
  'function createTargetSurfaceMaterial()',
  'attribute float fieldValueVm;',
  'color=vFieldVm>=uP95?uP95Color',
  'clipping:true',
  'function setBrainOutlineVisible(visible)',
  'for(const mesh of targetCoverageMeshes)mesh.visible=false',
  'metricThresholdSource=metricSource.thresholds||{}',
  'function coverageBandAllowed(band)'
]) {
  if (!viewer.includes(token))
    throw new Error(`Dynamic result viewer contract is missing: ${token}`);
}

for (const token of [
  'FemResultPackage.LoadAsync',
  'candidateOverlay.Matches(candidateVolume)',
  'File.Exists(resultPackage.Field3DPath)',
  'var requested = value && HasCompatible3DResult'
]) {
  if (!viewModel.includes(token))
    throw new Error(`View-model result guard is missing: ${token}`);
}

for (const token of [
  'PrepareViewerDataAsync',
  'GZipStream(source, CompressionMode.Decompress)',
  'CoreWebView2HostResourceAccessKind.Allow',
  '&data=',
  'root.GetProperty("thresholds").GetProperty("p95")'
]) {
  if (!viewCode.includes(token))
    throw new Error(`Dynamic viewer bridge is missing: ${token}`);
}
if (viewer.includes(
    'setCoverageCapVisible(entry.band,sideVisible&&entry.source.visible)'))
  throw new Error(
    'Colored clip caps still depend on hidden coverage-shell visibility.');
if (viewer.includes("geometry.setAttribute('color'"))
  throw new Error('Target surface still uses interpolated RGB vertex colors.');

for (const token of [
  'SHA256.HashDataAsync',
  'CryptographicOperations.FixedTimeEquals',
  'Path.GetRelativePath',
  'result-manifest.json'
]) {
  if (!packageLoader.includes(token))
    throw new Error(`Result package verification is missing: ${token}`);
}

const manifestPath = process.argv[2];
if (manifestPath) {
  const manifest = JSON.parse(fs.readFileSync(manifestPath, 'utf8'));
  if (![1, 2].includes(manifest.schema_version) || manifest.status !== 'PASS')
    throw new Error('Invalid test manifest header.');
  const packageDirectory = path.dirname(path.resolve(manifestPath));
  for (const [name, item] of Object.entries(manifest.files)) {
    const payloadPath = path.resolve(packageDirectory, item.path);
    const relative = path.relative(packageDirectory, payloadPath);
    if (relative.startsWith('..') || path.isAbsolute(relative))
      throw new Error(`${name} escapes the package directory.`);
    const digest = crypto.createHash('sha256')
      .update(fs.readFileSync(payloadPath)).digest('hex');
    if (digest !== item.sha256)
      throw new Error(`${name} SHA-256 mismatch.`);
  }
  const field3d = path.resolve(
    packageDirectory,
    manifest.files.field_3d.path);
  const payload = JSON.parse(zlib.gunzipSync(fs.readFileSync(field3d)));
  if (![1, 2, 3].includes(payload.schemaVersion) ||
      payload.subject !== manifest.subject_id)
    throw new Error('3D payload identity mismatch.');
  if (!Array.isArray(payload.structures) || payload.structures.length < 1)
    throw new Error('3D payload has no structures.');
  if (!Array.isArray(payload.field?.xyzMm) ||
      payload.field.xyzMm.length !== payload.field.valuesTiEnvelopeVm?.length)
    throw new Error('3D field point/value arrays are inconsistent.');
  if (payload.schemaVersion >= 2) {
    if (!Array.isArray(payload.worldToScene) ||
        payload.worldToScene.length !== 3)
      throw new Error('Schema-v2 payload has no world-to-scene transform.');
    const structures = new Map(
      payload.structures.map(item => [item.key, item]));
    for (const key of ['gray-matter', 'white-matter', 'scalp'])
      if (!structures.has(key))
        throw new Error(`Schema-v2 payload is missing ${key}.`);
    const gray = structures.get('gray-matter');
    if (!Array.isArray(gray.scalarValuesVm) ||
        gray.scalarValuesVm.length !== gray.verticesFlat.length / 3)
      throw new Error('Gray-matter FEM scalar array is inconsistent.');
    const amygdala = structures.get('amygdala');
    if (!amygdala ||
        !Array.isArray(amygdala.scalarValuesVm) ||
        amygdala.scalarValuesVm.length !== amygdala.verticesFlat.length / 3)
      throw new Error(
        'Amygdala surface FEM scalar array is missing or inconsistent.');
    const brainDisplay = payload.displaySurfaces?.brain;
    if (brainDisplay) {
      if (!Array.isArray(brainDisplay.verticesFlat) ||
          !Array.isArray(brainDisplay.trianglesFlat) ||
          !Array.isArray(brainDisplay.scalarValuesVm) ||
          brainDisplay.scalarValuesVm.length !==
            brainDisplay.verticesFlat.length / 3)
        throw new Error('Display-only brain surface/scalar arrays are inconsistent.');
      if (!brainDisplay.topology?.closed_edge_manifold)
        throw new Error('Display-only brain surface is not a closed manifold.');
    }
    if (!Array.isArray(payload.fieldShells) ||
        payload.fieldShells.length !== 2)
      throw new Error('Schema-v2 payload must contain P90/P95 field shells.');
  }
  console.log(
    `Verified subject ${manifest.subject_id}: ${payload.structures.length} ` +
    `structures, ${payload.field.xyzMm.length} field samples, all manifest ` +
    `hashes match.`);
}

console.log('WPF official-layout dynamic FEM package check passed.');
