import { generateKeyPairSync, sign, constants } from 'node:crypto';
import { readFileSync, writeFileSync } from 'node:fs';
const archive = process.argv[2];
const { privateKey, publicKey } = generateKeyPairSync('rsa', { modulusLength: 3072 });
const wrongKey = generateKeyPairSync('rsa', { modulusLength: 3072 }).privateKey;
const data = readFileSync(archive);
writeFileSync(archive + '.sig', sign('SHA256', data, { key: privateKey, padding: constants.RSA_PKCS1_PADDING }));
writeFileSync(archive + '.wrong.sig', sign('SHA256', data, { key: wrongKey, padding: constants.RSA_PKCS1_PADDING }));
writeFileSync(archive + '.public.json', JSON.stringify({ modulus: Buffer.from(publicKey.export({ format: 'jwk' }).n, 'base64url').toString('base64') }));
