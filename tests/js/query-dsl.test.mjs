// Run: node --test tests/js
import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as dsl from '../../src/LogLens.Viewer/ClientApp/src/lib/query-dsl.js';

test('tokenize matches QueryParser.Tokenize semantics (quotes anywhere in a token, kept)', () => {
  assert.deepEqual(dsl.tokenize('"a b" c -"d e"'), ['"a b"', 'c', '-"d e"']);
  assert.deepEqual(dsl.tokenize('App="Order Service" level:warning+'), ['App="Order Service"', 'level:warning+']);
  assert.deepEqual(dsl.tokenize('  x   y\t"z w"'), ['x', 'y', '"z w"']);
  assert.deepEqual(dsl.tokenize('"unterminated quote'), ['"unterminated quote']);
  assert.deepEqual(dsl.tokenize(''), []);
  assert.deepEqual(dsl.tokenize(undefined), []);
});

test('quote/unq round-trip with the server rule (inner quotes dropped)', () => {
  assert.equal(dsl.quote('plain'), 'plain');
  assert.equal(dsl.quote('two words'), '"two words"');
  assert.equal(dsl.quote('say "hi"'), '"say hi"');
  assert.equal(dsl.unq('"a b"'), 'a b');
  assert.equal(dsl.unq('*"a b"*'), '*a b*');
});

test('describeTerm renders plain English and folds negation once', () => {
  assert.equal(dsl.describeTerm('level:warning+').text, 'Level is at least Warning');
  assert.equal(dsl.describeTerm('level:err').text, 'Level is Error');
  assert.equal(dsl.describeTerm('-level:error').text, 'Level is not Error');
  assert.equal(dsl.describeTerm('OrderId=1002').text, 'OrderId is 1002');
  assert.equal(dsl.describeTerm('OrderId!=1002').text, 'OrderId is not 1002');
  assert.equal(dsl.describeTerm('-OrderId!=1002').text, 'OrderId is 1002');
  assert.equal(dsl.describeTerm('User~ann').text, 'User contains ann');
  assert.equal(dsl.describeTerm('-User~ann').text, 'User does not contain ann');
  assert.equal(dsl.describeTerm('Elapsed>=500').text, 'Elapsed ≥ 500');
  assert.equal(dsl.describeTerm('-Elapsed>500').text, 'Elapsed not > 500');
  assert.equal(dsl.describeTerm('has:exception').text, 'Exception is present');
  assert.equal(dsl.describeTerm('-has:RequestId').text, 'RequestId is absent');
  assert.equal(dsl.describeTerm('App="Order Service"').text, 'Resource is Order Service');
  assert.equal(dsl.describeTerm('source:demo').text, 'Resource is demo');
  assert.equal(dsl.describeTerm('category:Shop.*').text, 'Category matches Shop.*');
  assert.equal(dsl.describeTerm('message:timeout').text, 'Message contains timeout');
  assert.equal(dsl.describeTerm('"payment failed"').text, 'contains "payment failed"');
  assert.equal(dsl.describeTerm('-health').text, 'does not contain "health"');
  assert.equal(dsl.describeTerm('Key=a=b').value, 'a=b');
});

test('buildTerm → describeTerm agrees, and resource uses the real key', () => {
  const cases = [
    [['Level', 'is at least', 'Warning'], 'level:warning+'],
    [['Level', 'is not', 'Error'], '-level:error'],
    [['Category', 'contains', 'Shop'], 'category:*Shop*'],
    [['Message', 'does not contain', 'health check'], '-message:"health check"'],
    [['Exception', 'is present', ''], 'has:exception'],
    [['OrderId', 'is', '1002'], 'OrderId=1002'],
    [['User', 'does not contain', 'bot'], '-User~bot'],
    [['Elapsed', '≥', '500'], 'Elapsed>=500'],
    [['Route', 'is present', ''], 'has:Route'],
    [['Resource', 'is', 'Order Service', 'ServiceName'], 'ServiceName="Order Service"'],
    [['Resource', 'is', 'demo', null], 'source:demo'],
    [['Resource', 'is not', 'demo', null], '-source:demo'],
  ];
  for (const [args, expected] of cases) {
    const term = dsl.buildTerm(...args);
    assert.equal(term, expected, args.join(' '));
    assert.deepEqual(dsl.tokenize(term), [term], 'term must be a single token');
  }
  assert.equal(dsl.describeTerm(dsl.buildTerm('Level', 'is not', 'Error')).text, 'Level is not Error');
  assert.equal(dsl.describeTerm(dsl.buildTerm('Resource', 'is', 'Order Service', 'ServiceName')).text, 'Resource is Order Service');
});

test('resourceOf / traceOf precedence', () => {
  assert.deepEqual(dsl.resourceOf({ properties: { ServiceName: 'billing' }, source: 'http' }), { key: 'ServiceName', value: 'billing' });
  assert.deepEqual(dsl.resourceOf({ properties: {}, source: 'C:/logs/app.json' }), { key: null, value: 'C:/logs/app.json' });
  assert.deepEqual(dsl.traceOf({ properties: { RequestId: 'r', TraceId: 't' } }), { key: 'TraceId', value: 't' });
  assert.deepEqual(dsl.traceOf({ properties: { DocumentId: 'd', RequestId: 'r' } }), { key: 'DocumentId', value: 'd' });
  assert.equal(dsl.traceOf({ properties: {} }), null);
  assert.equal(dsl.resourceTerm(null, 'demo'), 'source:demo');
  assert.equal(dsl.resourceTerm('App', 'my app'), 'App="my app"');
});
