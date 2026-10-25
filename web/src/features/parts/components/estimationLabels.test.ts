import { describe, expect, it } from 'vitest';
import {
  estimationProfileNames,
  estimationSourceLabel,
} from './estimationLabels';

describe('estimationLabels', () => {
  it('names every estimate source, a sliced file by default', () => {
    expect(estimationSourceLabel('ProjectPresets')).toBe('Project presets');
    expect(estimationSourceLabel('EmbeddedProject')).toBe('Project settings');
    expect(estimationSourceLabel('DefaultProfiles')).toBe('Default profiles');
    expect(estimationSourceLabel('EmbeddedSlicedMetadata')).toBe('Sliced file');
  });

  it('lists the machine profile without repeating the model', () => {
    expect(
      estimationProfileNames({
        source: 'ProjectPresets',
        machineModel: 'Bambu Lab P1S',
        machineProfileName: 'Bambu Lab P1S 0.4 nozzle',
        processProfileName: '0.20mm Standard',
        filamentProfileNames: ['Aliz PETG'],
        buildPlate: 'High Temp Plate',
      }),
    ).toEqual([
      'Bambu Lab P1S 0.4 nozzle',
      '0.20mm Standard',
      'Aliz PETG',
      'High Temp Plate',
    ]);
  });
});
