const fs = require('fs');
const path = require('path');

const viewerDirectory = path.join(__dirname, '..', 'Assets', 'FemViewer');
const html = fs.readFileSync(path.join(viewerDirectory, 'fem-original-83y04.html'), 'utf8');
const viewerCss = fs.readFileSync(path.join(viewerDirectory, 'fem-viewer.css'), 'utf8');
const atlasText = fs.readFileSync(path.join(viewerDirectory, 'fem-atlas-meshes.js'), 'utf8');
const wpfViewCode = fs.readFileSync(path.join(__dirname, '..', 'Views', 'FemSimulationView.xaml.cs'), 'utf8');
const wpfViewXaml = fs.readFileSync(path.join(__dirname, '..', 'Views', 'FemSimulationView.xaml'), 'utf8');
const wpfViewModel = fs.readFileSync(path.join(__dirname, '..', 'ViewModels', 'FemSimulationViewModel.cs'), 'utf8');
const niftiVolume = fs.readFileSync(path.join(__dirname, '..', 'Services', 'HeadModel', 'NiftiVolume.cs'), 'utf8');
const sliceOverlay = fs.readFileSync(path.join(__dirname, '..', 'Services', 'Fem', 'FemSliceOverlay.cs'), 'utf8');
const dataMatch = html.match(/const data=(\{[\s\S]*?\});\s*const root=/);
const atlasMatch = atlasText.match(/^export const atlasMeshes=(\{[\s\S]*\});\s*$/);
if (!dataMatch) throw new Error('Embedded FEM data was not found.');
if (!atlasMatch) throw new Error('Atlas mesh module was not found.');
const data = JSON.parse(dataMatch[1]);
const atlas = JSON.parse(atlasMatch[1]);
const moduleMatch = html.match(/<script type="module">([\s\S]*?)<\/script>/);
if (!moduleMatch) throw new Error('Module script was not found.');

const checkableScript = moduleMatch[1].replace(/^import .*$/gm, '');
new Function(checkableScript);

const checkboxCount = (html.match(/class="structure-check-input"/g) || []).length;
if (checkboxCount !== 6) throw new Error(`Expected 6 structure checkboxes, found ${checkboxCount}.`);
if (!/value="amygdala" checked/.test(html)) throw new Error('Amygdala target must be selected by default.');
if (html.includes('structure-select')) throw new Error('Legacy structure select is still referenced.');
if (!html.includes("import { atlasMeshes } from './fem-atlas-meshes.js'")) throw new Error('Atlas mesh module is not imported.');
if (!/brainMaterial[^\n]*THREE\.DoubleSide/.test(html)) throw new Error('The full-view cortical material no longer preserves double-sided rendering.');
if (!/brain\.visible=false;[^\n]*contextMesh\.visible=true/.test(html)) throw new Error('Focus mode does not switch to the closed context shell.');
if (!/brain\.visible=true;[^\n]*contextMesh\.visible=false/.test(html)) throw new Error('Full-model restore does not restore the original cortex.');
if (!html.includes('fieldShellMeshes')) throw new Error('Continuous TI field shells are missing.');
if (atlas.regions.length !== 6) throw new Error(`Expected 6 regenerated atlas regions, found ${atlas.regions.length}.`);
if (!atlas.whiteMatter) throw new Error('Regenerated closed white-matter mesh is missing.');
if (/data\.(structures|whiteV|whiteF)/.test(moduleMatch[1])) throw new Error('Legacy internal meshes are still decoded by the viewer.');
if (!html.includes('for(const input of structureInputs)input.checked=true')) throw new Error('Focus mode does not select every brain region by default.');
if (!html.includes('id="select-all-structures"')) throw new Error('Select-all structure control is missing.');
if (!html.includes("selectAllStructures.addEventListener('change'")) throw new Error('Select-all structure interaction is missing.');
if (!html.includes('selectAllStructures.indeterminate=count>0&&count<structureInputs.length')) throw new Error('Select-all indeterminate state is missing.');
if (!html.includes('琥珀：P90–P95') || !html.includes('洋红：达到 P95')) throw new Error('The stimulation-field legend is missing.');
if (html.includes('show-field-shells')) throw new Error('Legacy combined field-shell toggle remains.');
if (!html.includes('id="show-field-outer"') || !html.includes('id="show-field-core"')) throw new Error('Independent P90/P95 field toggles are missing.');
if (!html.includes('for(const mesh of fieldShellMeshes){mesh.visible=showLens')) throw new Error('Target-neighborhood field visibility logic is missing.');
if (!html.includes('fieldLensMaterial') || !html.includes('toneMapped:false')) throw new Error('Field colors are still affected by scene lighting or tone mapping.');
if (!html.includes('contextMaterial.uniforms.uOpacity.value')) throw new Error('Rim-enhanced context opacity control is missing.');
if (!html.includes("document.getElementById('heat-threshold').disabled=true") || !html.includes("document.getElementById('heat-threshold').disabled=false")) throw new Error('Global threshold state is inconsistent with focus mode.');
if (!atlas.targetMetrics?.bilateral || !atlas.targetMetrics?.left || !atlas.targetMetrics?.right) throw new Error('Target FEM metrics are missing.');
for (const metric of [atlas.targetMetrics.bilateral, atlas.targetMetrics.left, atlas.targetMetrics.right]) {
  if (!Number.isFinite(metric.mean) || !Number.isFinite(metric.maximum) || metric.coverageP90 < 0 || metric.coverageP90 > 100 || metric.coverageP95 < 0 || metric.coverageP95 > 100) {
    throw new Error(`Invalid target FEM metric: ${JSON.stringify(metric)}`);
  }
}
if (atlas.schemaVersion < 2 || !Array.isArray(atlas.targetCoverageMeshes) || atlas.targetCoverageMeshes.length < 4) throw new Error('Amygdala coverage partition meshes are missing.');
if (html.includes('id="show-target-coverage"') || html.includes('show-coverage-p90')) throw new Error('Legacy threshold-partition mode is still exposed.');
if (!html.includes('function setTargetCoverageMode(enabled)') || !html.includes('id="show-p95-comparison"')) throw new Error('P95 comparison display mode is incomplete.');
if (!html.includes("mesh.userData.hemisphere=item.hemisphere") || !html.includes("mesh.userData.band=item.band")) throw new Error('Coverage hemisphere/band filtering is missing.');
if (!html.includes('id="show-p95-comparison"') || !html.includes('p95ComparisonMode') || !html.includes("uncovered:'#24384a'") || !html.includes("p95:'#ff2f7d'")) throw new Error('P95 covered/uncovered comparison mode is incomplete.');
if (!html.includes('targetEnvelopeMesh') || !html.includes('targetInterfaceMeshes') || !html.includes("targetOutline:'#50d6a0'")) throw new Error('P95 target envelope or boundary emphasis is missing.');
if (!html.includes('renderer.localClippingEnabled=true') || !html.includes('function updateCoverageClip()') || !html.includes('id="enable-coverage-clip"')) throw new Error('P95 GPU clipping controls are incomplete.');
for (const side of ['bilateral', 'left', 'right']) {
  const metric = atlas.targetMetrics[side];
  const total = metric.uncovered + metric.coverageP90Only + metric.coverageP95;
  if (Math.abs(total - 100) > 1e-6 || !Number.isFinite(metric.volumeMm3)) throw new Error(`Invalid mutually-exclusive coverage metric for ${side}.`);
}
if ((html.match(/class="btn view-preset"/g) || []).length !== 3) throw new Error('Expected three medical view presets.');
if (!html.includes('function updateLayerStatus()') || !html.includes('id="focus-layer-status"')) throw new Error('Visible-layer status feedback is missing.');
if (!html.includes("setMedicalView('coronal')") || !html.includes("view==='sagittal'") || !html.includes("view==='axial'")) throw new Error('Medical sagittal/coronal/axial view logic is incomplete.');
if (!html.includes('viewerBody.appendChild(focusTools)') || !/\.viewer-body\s*\{[\s\S]*?grid-template-columns:\s*minmax\(0, 1fr\) auto/.test(viewerCss) || !/\.focus-tools\s*\{[\s\S]*?position:\s*static/.test(viewerCss)) throw new Error('Standalone focus tools must use a non-overlapping side rail.');
if (!html.includes('id="toggle-focus-tools"') || !html.includes("classList.toggle('is-collapsed')") || !viewerCss.includes('.focus-tools.is-collapsed')) throw new Error('Collapsible focus tool panel is incomplete.');
if (!html.includes('id="medical-view-status"') || !html.includes('可自由旋转')) throw new Error('Medical view direction feedback is missing.');
if (!html.includes("postMessage('view:'")) throw new Error('Medical/free-rotation view state is not forwarded to WPF.');
if (!html.includes('id="medical-orientation"') || !html.includes('function updateMedicalOrientation(view)') || !html.includes('orientationOverlay.hidden=true')) throw new Error('Medical orientation markers are incomplete.');
if (!html.includes("view==='axial'?{top:'前',bottom:'后',left:'右',right:'左'")) throw new Error('Axial radiological orientation markers are incorrect.');
if (!html.includes('function detectManualRotation()') || !html.includes('actual.dot(expected)>.9995')) throw new Error('Zoom-safe medical orientation tracking is missing.');
if (!html.includes("get('host')==='wpf'") || !html.includes("root.classList.add('wpf-hosted')") || !viewerCss.includes('.wpf-hosted .viewer-toolbar')) throw new Error('WPF-hosted viewer does not hide its in-canvas controls.');
if (!html.includes("postMessage('metrics:'")) throw new Error('Target metrics are not forwarded to the WPF control panel.');
if (!html.includes("document.getElementById('show-white').checked=false")) throw new Error('Focus mode must start without the occluding white-matter layer.');
if (!html.includes('id="select-target-only"') || !html.includes("input.value==='amygdala'")) throw new Error('Target-only shortcut is missing.');
if (!html.includes('webglcontextlost')) throw new Error('WebGL context-loss handling is missing.');
if (!html.includes('function preferredPixelRatio(width,height)') || !html.includes('nextPixelRatio=preferredPixelRatio(w,h)')) throw new Error('Adaptive high-DPI rendering is missing.');
if (!html.includes('科研定量应使用原始四面体网格积分')) throw new Error('Preview-metric precision note is missing.');
if (!wpfViewCode.includes('versionedHostName') || !wpfViewCode.includes('newestAssetTicks')) throw new Error('WPF viewer cache invalidation is missing.');
if (!wpfViewCode.includes('?host=wpf') || !wpfViewCode.includes('&load=') || !wpfViewCode.includes('ExecuteViewerScriptAsync')) throw new Error('WPF-to-viewer control communication is missing.');
if (!wpfViewXaml.includes('x:Name="WpfThreeDimensionalControls"') || !wpfViewXaml.includes('x:Name="WpfFemFocusControls"')) throw new Error('3D controls are not hosted in the WPF sidebar.');
if (!wpfViewXaml.includes('x:Name="WpfCoverageOptions"') || wpfViewXaml.includes('x:Name="WpfTargetCoverageButton"') || !wpfViewCode.includes('SetCoverageModeUi')) throw new Error('WPF P95-only coverage controls are inconsistent.');
if (!wpfViewXaml.includes('x:Name="WpfP95ComparisonButton"') || !wpfViewXaml.includes('x:Name="WpfCoverageClipPosition"') || !wpfViewCode.includes('CoverageClipAxisClick')) throw new Error('WPF P95 comparison and clipping controls are incomplete.');
if (!/x:Name="WpfCoverageClipDetails"[^>]*Visibility="Collapsed"/.test(wpfViewXaml) || !html.includes('id="coverage-clip-details" hidden') || !wpfViewCode.includes('WpfCoverageClipDetails.Visibility')) throw new Error('Clip child controls are not conditionally displayed.');
if (!wpfViewXaml.includes('x:Name="WpfLeftP95Column"') || !wpfViewXaml.includes('x:Name="WpfRightP95Column"') || !wpfViewCode.includes('SetCoverageBar')) throw new Error('WPF coverage composition bars are missing.');
if (!wpfViewXaml.includes('x:Name="FemControlColumn"') || !wpfViewXaml.includes('x:Name="FemSidebarToggle"') || !wpfViewCode.includes('ToggleFemControlPanelClick')) throw new Error('The WPF control sidebar is not collapsible.');
if (!wpfViewXaml.includes('x:Name="WpfSelectAllStructures"') || !wpfViewXaml.includes('IsThreeState="True"') || !wpfViewCode.includes('UpdateWpfSelectAllState')) throw new Error('WPF structure select-all state is incomplete.');
if (!wpfViewXaml.includes('x:Name="WpfRestoreFullModelButton"') || !wpfViewCode.includes('WpfToggleScalpButton.IsEnabled = false')) throw new Error('WPF focus-mode action states are inconsistent.');
if (!wpfViewXaml.includes('x:Name="WpfLayerStatus"') || !wpfViewCode.includes('UpdateWpfLayerStatus')) throw new Error('WPF focus-layer summary is missing.');
if (!/x:Name="WpfFemFocusControls"[^>]*Visibility="Collapsed"/.test(wpfViewXaml) || !wpfViewXaml.includes('x:Name="WpfHeatThresholdPanel"') || !wpfViewCode.includes('WpfHeatThresholdPanel.Visibility = System.Windows.Visibility.Collapsed')) throw new Error('Focus-only WPF controls are not conditionally displayed.');
if (!wpfViewCode.includes('private async Task<bool> ExecuteViewerScriptAsync') || !wpfViewCode.includes('if (!await ClickViewerElementAsync("focus-stimulus")) return;')) throw new Error('WPF viewer commands do not guard UI updates on failure.');
if (!wpfViewCode.includes('right.GetProperty("coverageP95")') || wpfViewXaml.includes('WpfLeftP90Column') || !wpfViewXaml.includes('科研定量以原始四面体积分为准')) throw new Error('P95 target metrics or preview precision guidance is inconsistent.');
if ((wpfViewXaml.match(/PreviewMouseWheel="SlicePreviewMouseWheel"/g) || []).length !== 3 || !wpfViewCode.includes('Math.Clamp(transform.ScaleX') || !wpfViewCode.includes('SlicePanMove') || !wpfViewXaml.includes('SagittalZoomLabel') || !wpfViewXaml.includes('CoronalZoomLabel') || !wpfViewXaml.includes('AxialZoomLabel')) throw new Error('Independent 2D zoom and pan controls are incomplete.');
if (/Text="\{Binding VolumeInformation\}"[^>]*VerticalAlignment="Bottom"/.test(wpfViewXaml)) throw new Error('Volume information still overlays the visualization.');
if (!wpfViewCode.includes('StartViewerReadyTimeout') || !wpfViewCode.includes('TimeSpan.FromSeconds(20)')) throw new Error('WPF viewer ready timeout is missing.');
if (wpfViewCode.includes('CoreWebView2.Reload()')) throw new Error('WPF reload still reuses the cached viewer URL.');
if (!/x:Name="FemWebView"[^>]*Visibility="Collapsed"/.test(wpfViewXaml) || !/x:Name="FemViewerLoading"[^>]*Visibility="Visible"/.test(wpfViewXaml)) throw new Error('WPF initial loading state is inconsistent.');
if (!wpfViewModel.includes('resultPackage is not null') || !wpfViewModel.includes('File.Exists(resultPackage.Field3DPath)')) throw new Error('WPF does not guard 3D result-package identity.');
if (!wpfViewModel.includes('candidateOverlay.Matches(candidateVolume)')) throw new Error('WPF can still attach a field overlay to an unmatched MRI grid.');
if (!wpfViewModel.includes('Is3D = false;') || !wpfViewModel.includes('CurrentStep = 2;')) throw new Error('WPF does not open a validated result package in 2D first.');
if (!niftiVolume.includes('Build an isotropic, axis-aligned world grid') || !niftiVolume.includes('WorldToSourceVoxel') || !niftiVolume.includes('SampleSource')) throw new Error('MRI slices are no longer resampled to an orthogonal world grid.');
if (!niftiVolume.includes('BitConverter.ToInt16(header, 252) > 0') || !niftiVolume.includes('InvertAffine')) throw new Error('NIfTI qform/sform affine handling is incomplete.');
if (!sliceOverlay.includes('volume.SourceWidth == mriWidth') || !sliceOverlay.includes('GetDefaultSlices(NiftiVolume volume)') || !sliceOverlay.includes('volume.WorldToSourceVoxel')) throw new Error('ROI overlay is not mapped from the source grid to orthogonal slices.');
if (!wpfViewModel.includes('sliceOverlay.GetDefaultSlices(volume)')) throw new Error('Default ROI slices still use oblique source voxel indices.');
if (!wpfViewXaml.includes('Binding HasCompatible3DResult')) throw new Error('WPF 3D view is not bound to the compatible-result state.');
if (atlas.regions.filter(region => region.role === 'anatomy').some(region => region.color !== '#7893a6')) throw new Error('Anatomical region palette is not unified.');
if (atlas.fieldShells[0].color !== '#ffc928' || atlas.fieldShells[1].color !== '#ff3028') throw new Error('TI shells do not use the high-contrast field palette.');
if (/(applyCutaway|cutUniforms|focusMesh|slicePlane|\.discard;)/.test(html)) throw new Error('Legacy shader cutaway remains in the viewer.');
if (/id="show-white"[^>]*checked/.test(html)) throw new Error('White matter must not be forced on in focus mode.');
if (!html.includes('contextMesh.visible=false')) throw new Error('Focus context is visible before focus mode starts.');
if (!html.includes('whiteMatter.visible=false')) throw new Error('White matter is visible before focus mode starts.');

for (const report of atlas.validation) {
  if (report.boundaryEdges || report.nonmanifoldEdges || report.degenerateTriangles) {
    throw new Error(`Invalid closed mesh ${report.name}: ${JSON.stringify(report)}`);
  }
}
const atlasTriangles = atlas.validation.reduce((sum, item) => sum + item.triangles, 0);
console.log(`FEM viewer check passed: JavaScript syntax valid; ${checkboxCount} structure checkboxes; transparent atlas focus enabled.`);
console.log('Full-view invariant passed: original double-sided cortex is untouched; no shader discard or cut geometry remains.');
console.log(`Closed focus meshes: ${atlas.validation.length} meshes, ${atlasTriangles} triangles, 0 boundary/nonmanifold/degenerate elements.`);
console.log(`Base mesh: brain ${data.brainVertices}/${data.brainTriangles}; focus context ${atlas.contextShell.vertices}/${atlas.contextShell.triangles}.`);
