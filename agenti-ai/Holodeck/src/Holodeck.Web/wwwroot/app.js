import { registerApplet } from './workspace.js';
import { conversationsApplet } from './applets/conversations.js';
import { projectApplet } from './applets/project.js';
import { servicesApplet } from './applets/services.js';
import { libraryApplet } from './applets/library.js';
import { appearanceApplet } from './applets/appearance.js';
import { viewerApplet } from './applets/viewer.js';
import { startBoot } from './boot.js';

for (const applet of [conversationsApplet, projectApplet, servicesApplet, libraryApplet, appearanceApplet, viewerApplet]) { registerApplet(applet); }
startBoot();
