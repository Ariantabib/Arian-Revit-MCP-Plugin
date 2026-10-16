import { call, log } from './lib.mjs';
log('saved', JSON.stringify(await call('save_document', {})));
